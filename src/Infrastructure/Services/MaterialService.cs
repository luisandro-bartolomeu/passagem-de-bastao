using Core.DTOs;
using Core.Entities;
using Core.Enums;
using Core.Exceptions;
using Core.Interfaces;
using Core.Notifications;
using Infrastructure.Data;
using Infrastructure.Email;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace Infrastructure.Services;

public class MaterialService : IMaterialService
{
    private readonly AppDbContext _dbContext;
    private readonly IFileStorageService _fileStorageService;
    private readonly IEmailNotificationQueue _notificationQueue;
    private readonly EmailOptions _emailOptions;
    private readonly ILogger<MaterialService> _logger;

    public MaterialService(
        AppDbContext dbContext,
        IFileStorageService fileStorageService,
        IEmailNotificationQueue notificationQueue,
        IOptions<EmailOptions> emailOptions,
        ILogger<MaterialService> logger)
    {
        _dbContext = dbContext;
        _fileStorageService = fileStorageService;
        _notificationQueue = notificationQueue;
        _emailOptions = emailOptions.Value;
        _logger = logger;
    }

    public async Task<MaterialResponseDto> SubmitMaterialAsync(
        SubmitMaterialDto dto,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(dto);

        if (string.IsNullOrWhiteSpace(dto.Title))
        {
            throw new ValidationException("O título do material é obrigatório.");
        }

        if (string.IsNullOrWhiteSpace(dto.Description))
        {
            throw new ValidationException("A descrição do material é obrigatória.");
        }

        if (dto.FileStream == null || (dto.FileStream.CanSeek && dto.FileStream.Length == 0))
        {
            throw new ValidationException("É obrigatório fornecer um ficheiro válido.");
        }

        // 1. Verify subject exists
        var subject = await _dbContext.Subjects
            .FirstOrDefaultAsync(s => s.Id == dto.SubjectId, cancellationToken);

        if (subject == null)
        {
            throw new SubjectNotFoundException(dto.SubjectId);
        }

        // 2. Verify user exists and is active
        var user = await _dbContext.Users
            .FirstOrDefaultAsync(u => u.Id == dto.UploadedByUserId, cancellationToken);

        if (user == null || !user.IsActive)
        {
            throw new UnauthorizedAccessException("Utilizador não autenticado ou inativo.");
        }

        // 3. Upload physical file via IFileStorageService (validates magic bytes & 50MB limit)
        var storageResult = await _fileStorageService.UploadAsync(
            dto.FileStream,
            dto.FileName,
            dto.ContentType,
            cancellationToken);

        // 4. Instantiate Material entity with mandatory initial state 'Pending'
        var material = new Material
        {
            Id = Guid.NewGuid(),
            SubjectId = dto.SubjectId,
            UploadedByUserId = dto.UploadedByUserId,
            Title = dto.Title.Trim(),
            Description = dto.Description.Trim(),
            MaterialType = dto.MaterialType,
            FileStorageKey = storageResult.StorageKey,
            OriginalFileName = storageResult.OriginalFileName,
            ContentType = storageResult.ContentType,
            FileSizeBytes = storageResult.FileSizeBytes,
            Status = MaterialStatus.Pending, // RF03: Todo material submetido nasce Pendente
            RejectionReason = null,
            PeerHelperName = string.IsNullOrWhiteSpace(dto.PeerHelperName) ? null : dto.PeerHelperName.Trim(),
            PeerHelperContact = string.IsNullOrWhiteSpace(dto.PeerHelperContact) ? null : dto.PeerHelperContact.Trim(),
            CreatedAt = DateTime.UtcNow,
            UpdatedAt = null
        };

        try
        {
            _dbContext.Materials.Add(material);
            await _dbContext.SaveChangesAsync(cancellationToken);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Falha ao gravar material no banco. Removendo arquivo físico salvo...");
            await _fileStorageService.DeleteAsync(storageResult.StorageKey, cancellationToken);
            throw;
        }

        // 5. Enqueue administrative notification email asynchronously (RF04, RN04)
        try
        {
            var notification = new MaterialSubmittedNotification(
                MaterialId: material.Id,
                Title: material.Title,
                SubjectName: subject.Name,
                UploadedByUserName: user.Name,
                UploadedByUserEmail: user.Email,
                MaterialType: material.MaterialType,
                CreatedAt: material.CreatedAt
            );

            var emailMessage = notification.ToEmailMessage(_emailOptions.AdminEmail);
            await _notificationQueue.EnqueueAsync(emailMessage, cancellationToken);
            _logger.LogInformation("Evento de notificação do material {MaterialId} enfileirado para o Administrador.", material.Id);
        }
        catch (Exception ex)
        {
            // Do not fail the submission if enqueuing fails; log warning
            _logger.LogWarning(ex, "Falha ao enfileirar notificação de e-mail para material {MaterialId}", material.Id);
        }

        return MapToResponseDto(material, subject.Name, user.Name);
    }

    public async Task<MaterialResponseDto?> GetMaterialByIdAsync(
        Guid id,
        CancellationToken cancellationToken = default)
    {
        var material = await _dbContext.Materials
            .Include(m => m.Subject)
            .Include(m => m.UploadedByUser)
            .FirstOrDefaultAsync(m => m.Id == id, cancellationToken);

        if (material == null)
        {
            return null;
        }

        return MapToResponseDto(material, material.Subject.Name, material.UploadedByUser.Name);
    }

    public async Task<MaterialResponseDto> UpdateMaterialAsync(
        Guid id,
        UpdateMaterialDto dto,
        Guid currentUserId,
        UserRole currentUserRole,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(dto);

        if (string.IsNullOrWhiteSpace(dto.Title))
        {
            throw new ValidationException("O título do material é obrigatório.");
        }

        if (string.IsNullOrWhiteSpace(dto.Description))
        {
            throw new ValidationException("A descrição do material é obrigatória.");
        }

        var material = await _dbContext.Materials
            .Include(m => m.Subject)
            .Include(m => m.UploadedByUser)
            .FirstOrDefaultAsync(m => m.Id == id, cancellationToken);

        if (material == null)
        {
            throw new MaterialNotFoundException(id);
        }

        // Role-based permissions and lock checks (RF05, RN01, CB01)
        if (currentUserRole != UserRole.Admin)
        {
            if (material.UploadedByUserId != currentUserId)
            {
                throw new ForbiddenOperationException("Você não tem permissão para alterar materiais submetidos por outros utilizadores.");
            }

            if (material.Status != MaterialStatus.Pending)
            {
                throw new ForbiddenOperationException($"Materiais no estado '{material.Status}' não podem ser alterados por delegados. Apenas materiais pendentes são editáveis.");
            }
        }

        // Validate new subject if changed
        var subjectName = material.Subject.Name;
        if (dto.SubjectId != material.SubjectId)
        {
            var subject = await _dbContext.Subjects
                .FirstOrDefaultAsync(s => s.Id == dto.SubjectId, cancellationToken);

            if (subject == null)
            {
                throw new SubjectNotFoundException(dto.SubjectId);
            }

            material.SubjectId = dto.SubjectId;
            material.Subject = subject;
            subjectName = subject.Name;
        }

        material.Title = dto.Title.Trim();
        material.Description = dto.Description.Trim();
        material.MaterialType = dto.MaterialType;
        material.PeerHelperName = string.IsNullOrWhiteSpace(dto.PeerHelperName) ? null : dto.PeerHelperName.Trim();
        material.PeerHelperContact = string.IsNullOrWhiteSpace(dto.PeerHelperContact) ? null : dto.PeerHelperContact.Trim();
        material.UpdatedAt = DateTime.UtcNow;

        await _dbContext.SaveChangesAsync(cancellationToken);

        _logger.LogInformation("Material {MaterialId} atualizado com sucesso pelo utilizador {UserId} (Role: {Role}).",
            material.Id, currentUserId, currentUserRole);

        return MapToResponseDto(material, subjectName, material.UploadedByUser.Name);
    }

    public async Task DeleteMaterialAsync(
        Guid id,
        Guid currentUserId,
        UserRole currentUserRole,
        CancellationToken cancellationToken = default)
    {
        var material = await _dbContext.Materials
            .FirstOrDefaultAsync(m => m.Id == id, cancellationToken);

        if (material == null)
        {
            throw new MaterialNotFoundException(id);
        }

        // Role-based permissions and lock checks (RF05, RN01, CB01)
        if (currentUserRole != UserRole.Admin)
        {
            if (material.UploadedByUserId != currentUserId)
            {
                throw new ForbiddenOperationException("Você não tem permissão para eliminar materiais submetidos por outros utilizadores.");
            }

            if (material.Status != MaterialStatus.Pending)
            {
                throw new ForbiddenOperationException($"Materiais no estado '{material.Status}' não podem ser eliminados por delegados. Apenas materiais pendentes podem ser removidos.");
            }
        }

        var storageKey = material.FileStorageKey;

        _dbContext.Materials.Remove(material);
        await _dbContext.SaveChangesAsync(cancellationToken);

        // Physical file cleanup
        try
        {
            await _fileStorageService.DeleteAsync(storageKey, cancellationToken);
            _logger.LogInformation("Arquivo físico {StorageKey} do material {MaterialId} removido do armazenamento.", storageKey, id);
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Falha ao remover arquivo físico {StorageKey} do material {MaterialId}.", storageKey, id);
        }

        _logger.LogInformation("Material {MaterialId} excluído com sucesso pelo utilizador {UserId} (Role: {Role}).",
            id, currentUserId, currentUserRole);
    }

    public async Task<MaterialResponseDto> UpdateMaterialStatusAsync(
        Guid id,
        MaterialStatus newStatus,
        string? rejectionReason,
        CancellationToken cancellationToken = default)
    {
        if (newStatus != MaterialStatus.Published && newStatus != MaterialStatus.Rejected)
        {
            throw new ValidationException("O estado pretendido deve ser 'Published' ou 'Rejected'.");
        }

        if (newStatus == MaterialStatus.Rejected && string.IsNullOrWhiteSpace(rejectionReason))
        {
            throw new ValidationException("O motivo de rejeição é obrigatório ao rejeitar um material.");
        }

        var material = await _dbContext.Materials
            .Include(m => m.Subject)
            .Include(m => m.UploadedByUser)
            .FirstOrDefaultAsync(m => m.Id == id, cancellationToken);

        if (material == null)
        {
            throw new MaterialNotFoundException(id);
        }

        material.Status = newStatus;
        material.RejectionReason = newStatus == MaterialStatus.Rejected ? rejectionReason!.Trim() : null;
        material.UpdatedAt = DateTime.UtcNow;

        await _dbContext.SaveChangesAsync(cancellationToken);

        _logger.LogInformation("Estado do material {MaterialId} atualizado para {Status}. Motivo: {Reason}",
            material.Id, material.Status, material.RejectionReason);

        return MapToResponseDto(material, material.Subject.Name, material.UploadedByUser.Name);
    }

    public async Task<PagedResult<MaterialResponseDto>> GetPublishedMaterialsAsync(
        MaterialQueryFilter filter,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(filter);

        var query = _dbContext.Materials
            .Include(m => m.Subject)
            .Include(m => m.UploadedByUser)
            .AsNoTracking()
            .Where(m => m.Status == MaterialStatus.Published);

        if (filter.YearLevel.HasValue)
        {
            query = query.Where(m => m.Subject.YearLevel == filter.YearLevel.Value);
        }

        if (filter.SubjectId.HasValue)
        {
            query = query.Where(m => m.SubjectId == filter.SubjectId.Value);
        }

        if (filter.MaterialType.HasValue)
        {
            query = query.Where(m => m.MaterialType == filter.MaterialType.Value);
        }

        var totalCount = await query.CountAsync(cancellationToken);

        var page = filter.Page < 1 ? 1 : filter.Page;
        var pageSize = filter.PageSize < 1 ? 20 : (filter.PageSize > 50 ? 50 : filter.PageSize);
        var totalPages = totalCount == 0 ? 0 : (int)Math.Ceiling(totalCount / (double)pageSize);

        var materials = await query
            .OrderByDescending(m => m.CreatedAt)
            .Skip((page - 1) * pageSize)
            .Take(pageSize)
            .ToListAsync(cancellationToken);

        var items = materials
            .Select(m => MapToResponseDto(m, m.Subject.Name, m.UploadedByUser.Name))
            .ToList();

        return new PagedResult<MaterialResponseDto>(items, page, pageSize, totalCount, totalPages);
    }

    public async Task<MaterialDownloadDto?> GetMaterialForDownloadAsync(
        Guid id,
        CancellationToken cancellationToken = default)
    {
        var material = await _dbContext.Materials
            .AsNoTracking()
            .FirstOrDefaultAsync(m => m.Id == id && m.Status == MaterialStatus.Published, cancellationToken);

        if (material == null)
        {
            return null;
        }

        var downloadResult = await _fileStorageService.DownloadAsync(material.FileStorageKey, cancellationToken);
        if (downloadResult == null)
        {
            return null;
        }

        return new MaterialDownloadDto(
            Content: downloadResult.Content,
            ContentType: material.ContentType,
            OriginalFileName: material.OriginalFileName
        );
    }

    private static MaterialResponseDto MapToResponseDto(Material m, string subjectName, string userName)
    {
        return new MaterialResponseDto(
            Id: m.Id,
            SubjectId: m.SubjectId,
            SubjectName: subjectName,
            UploadedByUserId: m.UploadedByUserId,
            UploadedByUserName: userName,
            Title: m.Title,
            Description: m.Description,
            MaterialType: m.MaterialType.ToString(),
            FileStorageKey: m.FileStorageKey,
            OriginalFileName: m.OriginalFileName,
            ContentType: m.ContentType,
            FileSizeBytes: m.FileSizeBytes,
            Status: m.Status.ToString(),
            PeerHelperName: m.PeerHelperName,
            PeerHelperContact: m.PeerHelperContact,
            CreatedAt: m.CreatedAt,
            UpdatedAt: m.UpdatedAt,
            RejectionReason: m.RejectionReason
        );
    }
}
