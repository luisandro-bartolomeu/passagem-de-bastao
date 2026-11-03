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
            UpdatedAt: m.UpdatedAt
        );
    }
}
