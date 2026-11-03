using Core.DTOs;
using Core.Entities;
using Core.Enums;
using Core.Exceptions;
using Core.Interfaces;
using FluentAssertions;
using Infrastructure.Data;
using Infrastructure.Email;
using Infrastructure.Services;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;

namespace UnitTests;

public class MaterialServiceTests
{
    private class TestNotificationQueue : IEmailNotificationQueue
    {
        public List<EmailMessage> EnqueuedMessages { get; } = new();

        public ValueTask EnqueueAsync(EmailMessage message, CancellationToken cancellationToken = default)
        {
            EnqueuedMessages.Add(message);
            return ValueTask.CompletedTask;
        }

        public ValueTask<EmailMessage> DequeueAsync(CancellationToken cancellationToken = default)
        {
            return ValueTask.FromResult(EnqueuedMessages[0]);
        }
    }

    private class TestFileStorageService : IFileStorageService
    {
        public bool ShouldThrowInvalidFormat { get; set; }
        public bool ShouldThrowSizeLimit { get; set; }
        public List<string> UploadedKeys { get; } = new();
        public List<string> DeletedKeys { get; } = new();
        public Dictionary<string, byte[]> StoredFiles { get; } = new();

        public Task<FileStorageResult> UploadAsync(
            Stream content,
            string fileName,
            string contentType,
            CancellationToken cancellationToken = default)
        {
            if (ShouldThrowSizeLimit)
            {
                throw new FileSizeLimitExceededException(50 * 1024 * 1024);
            }

            if (ShouldThrowInvalidFormat)
            {
                throw new InvalidFileFormatException("Formato inválido ou magic bytes adulterados.");
            }

            var key = $"2026/11/{Guid.NewGuid():N}.pdf";
            UploadedKeys.Add(key);
            StoredFiles[key] = ValidPdfBytes;
            return Task.FromResult(new FileStorageResult(key, fileName, contentType, 1024));
        }

        public Task<FileDownloadResult?> DownloadAsync(string storageKey, CancellationToken cancellationToken = default)
        {
            if (StoredFiles.TryGetValue(storageKey, out var bytes))
            {
                return Task.FromResult<FileDownloadResult?>(new FileDownloadResult(new MemoryStream(bytes), "application/pdf", "download.pdf"));
            }

            return Task.FromResult<FileDownloadResult?>(null);
        }

        public Task<bool> DeleteAsync(string storageKey, CancellationToken cancellationToken = default)
        {
            DeletedKeys.Add(storageKey);
            StoredFiles.Remove(storageKey);
            return Task.FromResult(true);
        }

        public Task<string> GetFileUrlAsync(string storageKey, TimeSpan? expiry = null, CancellationToken cancellationToken = default) =>
            Task.FromResult($"http://localhost:5000/uploads/{storageKey}");
    }

    private AppDbContext CreateDbContext()
    {
        var options = new DbContextOptionsBuilder<AppDbContext>()
            .UseInMemoryDatabase("MaterialServiceTest_" + Guid.NewGuid().ToString("N"))
            .Options;
        return new AppDbContext(options);
    }

    private static readonly byte[] ValidPdfBytes = [0x25, 0x50, 0x44, 0x46, 0x2D, 0x31, 0x2E, 0x35];

    [Fact]
    public async Task SubmitMaterialAsync_WithValidData_ShouldCreateMaterialWithPendingStatusAndEnqueueEmail()
    {
        using var context = CreateDbContext();
        var user = new User
        {
            Id = Guid.NewGuid(),
            Name = "Manuel Delegado",
            Email = "manuel@isutic.ed.ao",
            Role = UserRole.Delegado,
            IsActive = true
        };
        var academicYear = new AcademicYear { Id = 1, YearLevel = 1, Name = "1.º Ano" };
        var subject = new Subject { Id = 10, YearLevel = 1, AcademicYear = academicYear, Code = "IGF101", Name = "Programação I" };

        context.Users.Add(user);
        context.AcademicYears.Add(academicYear);
        context.Subjects.Add(subject);
        await context.SaveChangesAsync();

        var storage = new TestFileStorageService();
        var queue = new TestNotificationQueue();
        var emailOptions = Options.Create(new EmailOptions { AdminEmail = "admin@isutic.ed.ao" });

        var sut = new MaterialService(context, storage, queue, emailOptions, NullLogger<MaterialService>.Instance);

        using var fileStream = new MemoryStream(ValidPdfBytes);
        var dto = new SubmitMaterialDto(
            Title: "Enunciado da Frequência 1",
            Description: "Enunciado oficial da prova de Programação I",
            MaterialType: MaterialType.Exam,
            SubjectId: 10,
            PeerHelperName: "Colega Ajudante",
            PeerHelperContact: "ajudante@isutic.ed.ao",
            FileStream: fileStream,
            FileName: "frequencia1.pdf",
            ContentType: "application/pdf",
            FileSizeBytes: ValidPdfBytes.Length,
            UploadedByUserId: user.Id
        );

        var result = await sut.SubmitMaterialAsync(dto);

        result.Should().NotBeNull();
        result.Title.Should().Be("Enunciado da Frequência 1");
        result.SubjectName.Should().Be("Programação I");
        result.UploadedByUserName.Should().Be("Manuel Delegado");
        result.Status.Should().Be("Pending"); // Mandatory initial status
        result.MaterialType.Should().Be("Exam");

        // Verify entity persisted in database
        var materialInDb = await context.Materials.FindAsync(result.Id);
        materialInDb.Should().NotBeNull();
        materialInDb!.Status.Should().Be(MaterialStatus.Pending);
        materialInDb.SubjectId.Should().Be(10);
        materialInDb.UploadedByUserId.Should().Be(user.Id);

        // Verify notification enqueued for administrator
        queue.EnqueuedMessages.Should().ContainSingle();
        var enqueuedEmail = queue.EnqueuedMessages[0];
        enqueuedEmail.To.Should().Be("admin@isutic.ed.ao");
        enqueuedEmail.Subject.Should().Contain("[IGF Académico] Novo Material Pendente: Enunciado da Frequência 1");
        enqueuedEmail.Body.Should().Contain("Programação I");
        enqueuedEmail.Body.Should().Contain("Manuel Delegado");
    }

    [Fact]
    public async Task SubmitMaterialAsync_WithNonExistentSubject_ShouldThrowSubjectNotFoundException()
    {
        using var context = CreateDbContext();
        var user = new User { Id = Guid.NewGuid(), Name = "Delegado", Email = "d@isutic.ed.ao", Role = UserRole.Delegado, IsActive = true };
        context.Users.Add(user);
        await context.SaveChangesAsync();

        var storage = new TestFileStorageService();
        var queue = new TestNotificationQueue();
        var sut = new MaterialService(context, storage, queue, Options.Create(new EmailOptions()), NullLogger<MaterialService>.Instance);

        using var fileStream = new MemoryStream(ValidPdfBytes);
        var dto = new SubmitMaterialDto(
            Title: "Título",
            Description: "Descrição",
            MaterialType: MaterialType.Exam,
            SubjectId: 9999, // Subject does not exist
            PeerHelperName: null,
            PeerHelperContact: null,
            FileStream: fileStream,
            FileName: "arquivo.pdf",
            ContentType: "application/pdf",
            FileSizeBytes: ValidPdfBytes.Length,
            UploadedByUserId: user.Id
        );

        var act = async () => await sut.SubmitMaterialAsync(dto);

        await act.Should().ThrowAsync<SubjectNotFoundException>()
            .Where(e => e.SubjectId == 9999);

        queue.EnqueuedMessages.Should().BeEmpty();
    }

    [Fact]
    public async Task SubmitMaterialAsync_WithInactiveUser_ShouldThrowUnauthorizedAccessException()
    {
        using var context = CreateDbContext();
        var inactiveUser = new User { Id = Guid.NewGuid(), Name = "Inactive", Email = "i@isutic.ed.ao", IsActive = false };
        var subject = new Subject { Id = 1, YearLevel = 1, Code = "IGF101", Name = "Prog" };
        context.Users.Add(inactiveUser);
        context.Subjects.Add(subject);
        await context.SaveChangesAsync();

        var sut = new MaterialService(context, new TestFileStorageService(), new TestNotificationQueue(), Options.Create(new EmailOptions()), NullLogger<MaterialService>.Instance);

        using var fileStream = new MemoryStream(ValidPdfBytes);
        var dto = new SubmitMaterialDto(
            Title: "Título",
            Description: "Descrição",
            MaterialType: MaterialType.Exam,
            SubjectId: 1,
            PeerHelperName: null,
            PeerHelperContact: null,
            FileStream: fileStream,
            FileName: "arquivo.pdf",
            ContentType: "application/pdf",
            FileSizeBytes: ValidPdfBytes.Length,
            UploadedByUserId: inactiveUser.Id
        );

        var act = async () => await sut.SubmitMaterialAsync(dto);

        await act.Should().ThrowAsync<UnauthorizedAccessException>();
    }

    [Theory]
    [InlineData("", "Descrição")]
    [InlineData("   ", "Descrição")]
    [InlineData("Título", "")]
    [InlineData("Título", "   ")]
    public async Task SubmitMaterialAsync_WithEmptyRequiredFields_ShouldThrowValidationException(string title, string description)
    {
        using var context = CreateDbContext();
        var sut = new MaterialService(context, new TestFileStorageService(), new TestNotificationQueue(), Options.Create(new EmailOptions()), NullLogger<MaterialService>.Instance);

        using var fileStream = new MemoryStream(ValidPdfBytes);
        var dto = new SubmitMaterialDto(
            Title: title,
            Description: description,
            MaterialType: MaterialType.Exam,
            SubjectId: 1,
            PeerHelperName: null,
            PeerHelperContact: null,
            FileStream: fileStream,
            FileName: "arquivo.pdf",
            ContentType: "application/pdf",
            FileSizeBytes: ValidPdfBytes.Length,
            UploadedByUserId: Guid.NewGuid()
        );

        var act = async () => await sut.SubmitMaterialAsync(dto);

        await act.Should().ThrowAsync<ValidationException>();
    }

    [Fact]
    public async Task GetMaterialByIdAsync_WithExistingId_ShouldReturnDto()
    {
        using var context = CreateDbContext();
        var user = new User { Id = Guid.NewGuid(), Name = "User 1", Email = "u1@isutic.ed.ao", Role = UserRole.Delegado, IsActive = true };
        var subject = new Subject { Id = 1, YearLevel = 1, Code = "IGF101", Name = "Cálculo" };
        var material = new Material
        {
            Id = Guid.NewGuid(),
            SubjectId = 1,
            Subject = subject,
            UploadedByUserId = user.Id,
            UploadedByUser = user,
            Title = "Livro de Exercícios",
            Description = "Resoluções",
            MaterialType = MaterialType.LectureNote,
            FileStorageKey = "2026/11/abc.pdf",
            OriginalFileName = "livro.pdf",
            ContentType = "application/pdf",
            FileSizeBytes = 2048,
            Status = MaterialStatus.Pending,
            CreatedAt = DateTime.UtcNow
        };

        context.Users.Add(user);
        context.Subjects.Add(subject);
        context.Materials.Add(material);
        await context.SaveChangesAsync();

        var sut = new MaterialService(context, new TestFileStorageService(), new TestNotificationQueue(), Options.Create(new EmailOptions()), NullLogger<MaterialService>.Instance);

        var result = await sut.GetMaterialByIdAsync(material.Id);

        result.Should().NotBeNull();
        result!.Id.Should().Be(material.Id);
        result.Title.Should().Be("Livro de Exercícios");
        result.SubjectName.Should().Be("Cálculo");
        result.UploadedByUserName.Should().Be("User 1");
    }

    [Fact]
    public async Task GetMaterialByIdAsync_WithNonExistentId_ShouldReturnNull()
    {
        using var context = CreateDbContext();
        var sut = new MaterialService(context, new TestFileStorageService(), new TestNotificationQueue(), Options.Create(new EmailOptions()), NullLogger<MaterialService>.Instance);

        var result = await sut.GetMaterialByIdAsync(Guid.NewGuid());

        result.Should().BeNull();
    }

    [Fact]
    public async Task UpdateMaterialAsync_AsOwnerDelegado_WhenPending_ShouldUpdateSuccessfully()
    {
        using var context = CreateDbContext();
        var user = new User { Id = Guid.NewGuid(), Name = "Delegado A", Email = "da@isutic.ed.ao", Role = UserRole.Delegado, IsActive = true };
        var subject1 = new Subject { Id = 1, YearLevel = 1, Code = "IGF101", Name = "Cálculo I" };
        var subject2 = new Subject { Id = 2, YearLevel = 1, Code = "IGF102", Name = "Programação I" };
        var material = new Material
        {
            Id = Guid.NewGuid(),
            SubjectId = 1,
            Subject = subject1,
            UploadedByUserId = user.Id,
            UploadedByUser = user,
            Title = "Título Antigo",
            Description = "Descrição Antiga",
            MaterialType = MaterialType.Exam,
            FileStorageKey = "keys/file.pdf",
            OriginalFileName = "file.pdf",
            ContentType = "application/pdf",
            FileSizeBytes = 1000,
            Status = MaterialStatus.Pending,
            CreatedAt = DateTime.UtcNow.AddHours(-1)
        };

        context.Users.Add(user);
        context.Subjects.AddRange(subject1, subject2);
        context.Materials.Add(material);
        await context.SaveChangesAsync();

        var sut = new MaterialService(context, new TestFileStorageService(), new TestNotificationQueue(), Options.Create(new EmailOptions()), NullLogger<MaterialService>.Instance);

        var updateDto = new UpdateMaterialDto(
            Title: "Título Novo",
            Description: "Descrição Nova",
            MaterialType: MaterialType.LectureNote,
            SubjectId: 2,
            PeerHelperName: "Monitor Carlos",
            PeerHelperContact: "carlos@isutic.ed.ao"
        );

        var result = await sut.UpdateMaterialAsync(material.Id, updateDto, user.Id, UserRole.Delegado);

        result.Title.Should().Be("Título Novo");
        result.Description.Should().Be("Descrição Nova");
        result.MaterialType.Should().Be("LectureNote");
        result.SubjectId.Should().Be(2);
        result.SubjectName.Should().Be("Programação I");
        result.PeerHelperName.Should().Be("Monitor Carlos");
        result.UpdatedAt.Should().NotBeNull();
    }

    [Fact]
    public async Task UpdateMaterialAsync_AsOwnerDelegado_WhenPublished_ShouldThrowForbiddenOperationException()
    {
        using var context = CreateDbContext();
        var user = new User { Id = Guid.NewGuid(), Name = "Delegado A", Email = "da@isutic.ed.ao", Role = UserRole.Delegado, IsActive = true };
        var subject = new Subject { Id = 1, YearLevel = 1, Code = "IGF101", Name = "Cálculo I" };
        var material = new Material
        {
            Id = Guid.NewGuid(),
            SubjectId = 1,
            Subject = subject,
            UploadedByUserId = user.Id,
            UploadedByUser = user,
            Title = "Título",
            Description = "Descrição",
            MaterialType = MaterialType.Exam,
            FileStorageKey = "keys/file.pdf",
            OriginalFileName = "file.pdf",
            ContentType = "application/pdf",
            FileSizeBytes = 1000,
            Status = MaterialStatus.Published, // Already published!
            CreatedAt = DateTime.UtcNow
        };

        context.Users.Add(user);
        context.Subjects.Add(subject);
        context.Materials.Add(material);
        await context.SaveChangesAsync();

        var sut = new MaterialService(context, new TestFileStorageService(), new TestNotificationQueue(), Options.Create(new EmailOptions()), NullLogger<MaterialService>.Instance);
        var updateDto = new UpdateMaterialDto("Novo Título", "Nova Descrição", MaterialType.Exam, 1, null, null);

        var act = async () => await sut.UpdateMaterialAsync(material.Id, updateDto, user.Id, UserRole.Delegado);

        var ex = await act.Should().ThrowAsync<ForbiddenOperationException>();
        ex.WithMessage("*não podem ser alterados por delegados*");
    }

    [Fact]
    public async Task UpdateMaterialAsync_AsAnotherDelegado_ShouldThrowForbiddenOperationException()
    {
        using var context = CreateDbContext();
        var owner = new User { Id = Guid.NewGuid(), Name = "Owner Delegado", Email = "owner@isutic.ed.ao", Role = UserRole.Delegado, IsActive = true };
        var otherDelegado = new User { Id = Guid.NewGuid(), Name = "Outro Delegado", Email = "other@isutic.ed.ao", Role = UserRole.Delegado, IsActive = true };
        var subject = new Subject { Id = 1, YearLevel = 1, Code = "IGF101", Name = "Cálculo I" };
        var material = new Material
        {
            Id = Guid.NewGuid(),
            SubjectId = 1,
            Subject = subject,
            UploadedByUserId = owner.Id,
            UploadedByUser = owner,
            Title = "Título",
            Description = "Descrição",
            MaterialType = MaterialType.Exam,
            FileStorageKey = "keys/file.pdf",
            OriginalFileName = "file.pdf",
            ContentType = "application/pdf",
            FileSizeBytes = 1000,
            Status = MaterialStatus.Pending,
            CreatedAt = DateTime.UtcNow
        };

        context.Users.AddRange(owner, otherDelegado);
        context.Subjects.Add(subject);
        context.Materials.Add(material);
        await context.SaveChangesAsync();

        var sut = new MaterialService(context, new TestFileStorageService(), new TestNotificationQueue(), Options.Create(new EmailOptions()), NullLogger<MaterialService>.Instance);
        var updateDto = new UpdateMaterialDto("Novo Título", "Nova Descrição", MaterialType.Exam, 1, null, null);

        var act = async () => await sut.UpdateMaterialAsync(material.Id, updateDto, otherDelegado.Id, UserRole.Delegado);

        var ex = await act.Should().ThrowAsync<ForbiddenOperationException>();
        ex.WithMessage("*submetidos por outros utilizadores*");
    }

    [Fact]
    public async Task UpdateMaterialAsync_AsAdmin_WhenMaterialIsPublished_ShouldUpdateSuccessfully()
    {
        using var context = CreateDbContext();
        var owner = new User { Id = Guid.NewGuid(), Name = "Delegado Owner", Email = "owner@isutic.ed.ao", Role = UserRole.Delegado, IsActive = true };
        var admin = new User { Id = Guid.NewGuid(), Name = "Admin Chefe", Email = "admin@isutic.ed.ao", Role = UserRole.Admin, IsActive = true };
        var subject = new Subject { Id = 1, YearLevel = 1, Code = "IGF101", Name = "Cálculo I" };
        var material = new Material
        {
            Id = Guid.NewGuid(),
            SubjectId = 1,
            Subject = subject,
            UploadedByUserId = owner.Id,
            UploadedByUser = owner,
            Title = "Título Inicial",
            Description = "Descrição Inicial",
            MaterialType = MaterialType.Exam,
            FileStorageKey = "keys/file.pdf",
            OriginalFileName = "file.pdf",
            ContentType = "application/pdf",
            FileSizeBytes = 1000,
            Status = MaterialStatus.Published, // Published!
            CreatedAt = DateTime.UtcNow
        };

        context.Users.AddRange(owner, admin);
        context.Subjects.Add(subject);
        context.Materials.Add(material);
        await context.SaveChangesAsync();

        var sut = new MaterialService(context, new TestFileStorageService(), new TestNotificationQueue(), Options.Create(new EmailOptions()), NullLogger<MaterialService>.Instance);
        var updateDto = new UpdateMaterialDto("Título Atualizado pelo Admin", "Descrição Editada", MaterialType.LectureNote, 1, null, null);

        var result = await sut.UpdateMaterialAsync(material.Id, updateDto, admin.Id, UserRole.Admin);

        result.Title.Should().Be("Título Atualizado pelo Admin");
        result.MaterialType.Should().Be("LectureNote");
        result.UpdatedAt.Should().NotBeNull();
    }

    [Fact]
    public async Task UpdateMaterialAsync_WithNonExistentMaterial_ShouldThrowMaterialNotFoundException()
    {
        using var context = CreateDbContext();
        var sut = new MaterialService(context, new TestFileStorageService(), new TestNotificationQueue(), Options.Create(new EmailOptions()), NullLogger<MaterialService>.Instance);
        var updateDto = new UpdateMaterialDto("Título", "Descrição", MaterialType.Exam, 1, null, null);

        var act = async () => await sut.UpdateMaterialAsync(Guid.NewGuid(), updateDto, Guid.NewGuid(), UserRole.Admin);

        await act.Should().ThrowAsync<MaterialNotFoundException>();
    }

    [Fact]
    public async Task DeleteMaterialAsync_AsOwnerDelegado_WhenPending_ShouldDeleteAndCleanFileStorage()
    {
        using var context = CreateDbContext();
        var fileStorage = new TestFileStorageService();
        var user = new User { Id = Guid.NewGuid(), Name = "Delegado A", Email = "da@isutic.ed.ao", Role = UserRole.Delegado, IsActive = true };
        var subject = new Subject { Id = 1, YearLevel = 1, Code = "IGF101", Name = "Cálculo I" };
        var storageKey = "keys/material-para-apagar.pdf";
        var material = new Material
        {
            Id = Guid.NewGuid(),
            SubjectId = 1,
            Subject = subject,
            UploadedByUserId = user.Id,
            UploadedByUser = user,
            Title = "Para Apagar",
            Description = "Descrição",
            MaterialType = MaterialType.Exam,
            FileStorageKey = storageKey,
            OriginalFileName = "file.pdf",
            ContentType = "application/pdf",
            FileSizeBytes = 1000,
            Status = MaterialStatus.Pending,
            CreatedAt = DateTime.UtcNow
        };

        context.Users.Add(user);
        context.Subjects.Add(subject);
        context.Materials.Add(material);
        await context.SaveChangesAsync();

        var sut = new MaterialService(context, fileStorage, new TestNotificationQueue(), Options.Create(new EmailOptions()), NullLogger<MaterialService>.Instance);

        await sut.DeleteMaterialAsync(material.Id, user.Id, UserRole.Delegado);

        var deletedEntity = await context.Materials.FindAsync(material.Id);
        deletedEntity.Should().BeNull();
        fileStorage.DeletedKeys.Should().Contain(storageKey);
    }

    [Fact]
    public async Task DeleteMaterialAsync_AsOwnerDelegado_WhenPublished_ShouldThrowForbiddenOperationException()
    {
        using var context = CreateDbContext();
        var user = new User { Id = Guid.NewGuid(), Name = "Delegado A", Email = "da@isutic.ed.ao", Role = UserRole.Delegado, IsActive = true };
        var subject = new Subject { Id = 1, YearLevel = 1, Code = "IGF101", Name = "Cálculo I" };
        var material = new Material
        {
            Id = Guid.NewGuid(),
            SubjectId = 1,
            Subject = subject,
            UploadedByUserId = user.Id,
            UploadedByUser = user,
            Title = "Material Publicado",
            Description = "Descrição",
            MaterialType = MaterialType.Exam,
            FileStorageKey = "keys/published.pdf",
            OriginalFileName = "file.pdf",
            ContentType = "application/pdf",
            FileSizeBytes = 1000,
            Status = MaterialStatus.Published,
            CreatedAt = DateTime.UtcNow
        };

        context.Users.Add(user);
        context.Subjects.Add(subject);
        context.Materials.Add(material);
        await context.SaveChangesAsync();

        var sut = new MaterialService(context, new TestFileStorageService(), new TestNotificationQueue(), Options.Create(new EmailOptions()), NullLogger<MaterialService>.Instance);

        var act = async () => await sut.DeleteMaterialAsync(material.Id, user.Id, UserRole.Delegado);

        var ex = await act.Should().ThrowAsync<ForbiddenOperationException>();
        ex.WithMessage("*não podem ser eliminados por delegados*");
    }

    [Fact]
    public async Task DeleteMaterialAsync_AsAnotherDelegado_ShouldThrowForbiddenOperationException()
    {
        using var context = CreateDbContext();
        var owner = new User { Id = Guid.NewGuid(), Name = "Delegado 1", Email = "d1@isutic.ed.ao", Role = UserRole.Delegado, IsActive = true };
        var other = new User { Id = Guid.NewGuid(), Name = "Delegado 2", Email = "d2@isutic.ed.ao", Role = UserRole.Delegado, IsActive = true };
        var subject = new Subject { Id = 1, YearLevel = 1, Code = "IGF101", Name = "Cálculo I" };
        var material = new Material
        {
            Id = Guid.NewGuid(),
            SubjectId = 1,
            Subject = subject,
            UploadedByUserId = owner.Id,
            UploadedByUser = owner,
            Title = "Pendente",
            Description = "Descrição",
            MaterialType = MaterialType.Exam,
            FileStorageKey = "keys/p.pdf",
            OriginalFileName = "file.pdf",
            ContentType = "application/pdf",
            FileSizeBytes = 1000,
            Status = MaterialStatus.Pending,
            CreatedAt = DateTime.UtcNow
        };

        context.Users.AddRange(owner, other);
        context.Subjects.Add(subject);
        context.Materials.Add(material);
        await context.SaveChangesAsync();

        var sut = new MaterialService(context, new TestFileStorageService(), new TestNotificationQueue(), Options.Create(new EmailOptions()), NullLogger<MaterialService>.Instance);

        var act = async () => await sut.DeleteMaterialAsync(material.Id, other.Id, UserRole.Delegado);

        var ex = await act.Should().ThrowAsync<ForbiddenOperationException>();
        ex.WithMessage("*submetidos por outros utilizadores*");
    }

    [Fact]
    public async Task DeleteMaterialAsync_AsAdmin_WhenMaterialIsPublished_ShouldDeleteSuccessfully()
    {
        using var context = CreateDbContext();
        var fileStorage = new TestFileStorageService();
        var owner = new User { Id = Guid.NewGuid(), Name = "Delegado 1", Email = "d1@isutic.ed.ao", Role = UserRole.Delegado, IsActive = true };
        var admin = new User { Id = Guid.NewGuid(), Name = "Admin", Email = "admin@isutic.ed.ao", Role = UserRole.Admin, IsActive = true };
        var subject = new Subject { Id = 1, YearLevel = 1, Code = "IGF101", Name = "Cálculo I" };
        var storageKey = "keys/admin-delete-published.pdf";
        var material = new Material
        {
            Id = Guid.NewGuid(),
            SubjectId = 1,
            Subject = subject,
            UploadedByUserId = owner.Id,
            UploadedByUser = owner,
            Title = "Publicado para Admin Apagar",
            Description = "Descrição",
            MaterialType = MaterialType.Exam,
            FileStorageKey = storageKey,
            OriginalFileName = "file.pdf",
            ContentType = "application/pdf",
            FileSizeBytes = 1000,
            Status = MaterialStatus.Published,
            CreatedAt = DateTime.UtcNow
        };

        context.Users.AddRange(owner, admin);
        context.Subjects.Add(subject);
        context.Materials.Add(material);
        await context.SaveChangesAsync();

        var sut = new MaterialService(context, fileStorage, new TestNotificationQueue(), Options.Create(new EmailOptions()), NullLogger<MaterialService>.Instance);

        await sut.DeleteMaterialAsync(material.Id, admin.Id, UserRole.Admin);

        var deletedEntity = await context.Materials.FindAsync(material.Id);
        deletedEntity.Should().BeNull();
        fileStorage.DeletedKeys.Should().Contain(storageKey);
    }

    [Fact]
    public async Task DeleteMaterialAsync_WithNonExistentId_ShouldThrowMaterialNotFoundException()
    {
        using var context = CreateDbContext();
        var sut = new MaterialService(context, new TestFileStorageService(), new TestNotificationQueue(), Options.Create(new EmailOptions()), NullLogger<MaterialService>.Instance);

        var act = async () => await sut.DeleteMaterialAsync(Guid.NewGuid(), Guid.NewGuid(), UserRole.Admin);

        await act.Should().ThrowAsync<MaterialNotFoundException>();
    }

    [Fact]
    public async Task UpdateMaterialStatusAsync_ApprovePendingMaterial_ShouldTransitionToPublishedAndClearReason()
    {
        using var context = CreateDbContext();
        var user = new User { Id = Guid.NewGuid(), Name = "Delegado Silva", Email = "del@isutic.ed.ao", Role = UserRole.Delegado, IsActive = true };
        var subject = new Subject { Id = 1, YearLevel = 1, Code = "IGF101", Name = "Cálculo I" };
        var material = new Material
        {
            Id = Guid.NewGuid(),
            SubjectId = 1,
            Subject = subject,
            UploadedByUserId = user.Id,
            UploadedByUser = user,
            Title = "Exercícios",
            Description = "Lista 1",
            MaterialType = MaterialType.Exam,
            FileStorageKey = "keys/mat1.pdf",
            OriginalFileName = "mat1.pdf",
            ContentType = "application/pdf",
            FileSizeBytes = 1000,
            Status = MaterialStatus.Pending,
            CreatedAt = DateTime.UtcNow
        };

        context.Users.Add(user);
        context.Subjects.Add(subject);
        context.Materials.Add(material);
        await context.SaveChangesAsync();

        var sut = new MaterialService(context, new TestFileStorageService(), new TestNotificationQueue(), Options.Create(new EmailOptions()), NullLogger<MaterialService>.Instance);

        var result = await sut.UpdateMaterialStatusAsync(material.Id, MaterialStatus.Published, null);

        result.Status.Should().Be("Published");
        result.RejectionReason.Should().BeNull();
        result.UpdatedAt.Should().NotBeNull();

        var updatedEntity = await context.Materials.FindAsync(material.Id);
        updatedEntity!.Status.Should().Be(MaterialStatus.Published);
        updatedEntity.RejectionReason.Should().BeNull();
    }

    [Fact]
    public async Task UpdateMaterialStatusAsync_RejectPendingMaterialWithReason_ShouldTransitionToRejectedAndStoreReason()
    {
        using var context = CreateDbContext();
        var user = new User { Id = Guid.NewGuid(), Name = "Delegado Silva", Email = "del@isutic.ed.ao", Role = UserRole.Delegado, IsActive = true };
        var subject = new Subject { Id = 1, YearLevel = 1, Code = "IGF101", Name = "Cálculo I" };
        var material = new Material
        {
            Id = Guid.NewGuid(),
            SubjectId = 1,
            Subject = subject,
            UploadedByUserId = user.Id,
            UploadedByUser = user,
            Title = "Sebenta",
            Description = "Sebenta",
            MaterialType = MaterialType.LectureNote,
            FileStorageKey = "keys/seb1.pdf",
            OriginalFileName = "seb1.pdf",
            ContentType = "application/pdf",
            FileSizeBytes = 1000,
            Status = MaterialStatus.Pending,
            CreatedAt = DateTime.UtcNow
        };

        context.Users.Add(user);
        context.Subjects.Add(subject);
        context.Materials.Add(material);
        await context.SaveChangesAsync();

        var sut = new MaterialService(context, new TestFileStorageService(), new TestNotificationQueue(), Options.Create(new EmailOptions()), NullLogger<MaterialService>.Instance);

        var reason = "Arquivo incompleto faltando páginas 10 a 20.";
        var result = await sut.UpdateMaterialStatusAsync(material.Id, MaterialStatus.Rejected, reason);

        result.Status.Should().Be("Rejected");
        result.RejectionReason.Should().Be(reason);
        result.UpdatedAt.Should().NotBeNull();

        var updatedEntity = await context.Materials.FindAsync(material.Id);
        updatedEntity!.Status.Should().Be(MaterialStatus.Rejected);
        updatedEntity.RejectionReason.Should().Be(reason);
    }

    [Fact]
    public async Task UpdateMaterialStatusAsync_RejectWithoutReason_ShouldThrowValidationException()
    {
        using var context = CreateDbContext();
        var sut = new MaterialService(context, new TestFileStorageService(), new TestNotificationQueue(), Options.Create(new EmailOptions()), NullLogger<MaterialService>.Instance);

        var actNull = async () => await sut.UpdateMaterialStatusAsync(Guid.NewGuid(), MaterialStatus.Rejected, null);
        var actEmpty = async () => await sut.UpdateMaterialStatusAsync(Guid.NewGuid(), MaterialStatus.Rejected, "   ");

        var exNull = await actNull.Should().ThrowAsync<ValidationException>();
        exNull.WithMessage("*motivo de rejeição é obrigatório*");

        var exEmpty = await actEmpty.Should().ThrowAsync<ValidationException>();
        exEmpty.WithMessage("*motivo de rejeição é obrigatório*");
    }

    [Fact]
    public async Task UpdateMaterialStatusAsync_WithInvalidStatus_ShouldThrowValidationException()
    {
        using var context = CreateDbContext();
        var sut = new MaterialService(context, new TestFileStorageService(), new TestNotificationQueue(), Options.Create(new EmailOptions()), NullLogger<MaterialService>.Instance);

        var act = async () => await sut.UpdateMaterialStatusAsync(Guid.NewGuid(), MaterialStatus.Pending, null);

        var ex = await act.Should().ThrowAsync<ValidationException>();
        ex.WithMessage("*deve ser 'Published' ou 'Rejected'*");
    }

    [Fact]
    public async Task UpdateMaterialStatusAsync_WithNonExistentId_ShouldThrowMaterialNotFoundException()
    {
        using var context = CreateDbContext();
        var sut = new MaterialService(context, new TestFileStorageService(), new TestNotificationQueue(), Options.Create(new EmailOptions()), NullLogger<MaterialService>.Instance);

        var act = async () => await sut.UpdateMaterialStatusAsync(Guid.NewGuid(), MaterialStatus.Published, null);

        await act.Should().ThrowAsync<MaterialNotFoundException>();
    }

    [Fact]
    public async Task UpdateMaterialStatusAsync_ApprovePreviouslyRejectedMaterial_ShouldClearRejectionReason()
    {
        using var context = CreateDbContext();
        var user = new User { Id = Guid.NewGuid(), Name = "Delegado Silva", Email = "del@isutic.ed.ao", Role = UserRole.Delegado, IsActive = true };
        var subject = new Subject { Id = 1, YearLevel = 1, Code = "IGF101", Name = "Cálculo I" };
        var material = new Material
        {
            Id = Guid.NewGuid(),
            SubjectId = 1,
            Subject = subject,
            UploadedByUserId = user.Id,
            UploadedByUser = user,
            Title = "Sebenta Rejeitada",
            Description = "Sebenta",
            MaterialType = MaterialType.LectureNote,
            FileStorageKey = "keys/seb1.pdf",
            OriginalFileName = "seb1.pdf",
            ContentType = "application/pdf",
            FileSizeBytes = 1000,
            Status = MaterialStatus.Rejected,
            RejectionReason = "Motivo antigo",
            CreatedAt = DateTime.UtcNow
        };

        context.Users.Add(user);
        context.Subjects.Add(subject);
        context.Materials.Add(material);
        await context.SaveChangesAsync();

        var sut = new MaterialService(context, new TestFileStorageService(), new TestNotificationQueue(), Options.Create(new EmailOptions()), NullLogger<MaterialService>.Instance);

        var result = await sut.UpdateMaterialStatusAsync(material.Id, MaterialStatus.Published, null);

        result.Status.Should().Be("Published");
        result.RejectionReason.Should().BeNull();

        var updatedEntity = await context.Materials.FindAsync(material.Id);
        updatedEntity!.Status.Should().Be(MaterialStatus.Published);
        updatedEntity.RejectionReason.Should().BeNull();
    }

    [Fact]
    public async Task GetPublishedMaterialsAsync_ShouldOnlyReturnPublishedMaterials_AndExcludePendingAndRejected()
    {
        using var context = CreateDbContext();
        var user = new User { Id = Guid.NewGuid(), Name = "User 1", Email = "u1@isutic.ed.ao", Role = UserRole.Delegado, IsActive = true };
        var subject = new Subject { Id = 1, YearLevel = 1, Code = "IGF101", Name = "Cálculo I" };

        var published1 = new Material
        {
            Id = Guid.NewGuid(), SubjectId = 1, Subject = subject, UploadedByUserId = user.Id, UploadedByUser = user,
            Title = "Pub 1", Description = "Desc", MaterialType = MaterialType.Exam,
            FileStorageKey = "k1", OriginalFileName = "p1.pdf", ContentType = "application/pdf", FileSizeBytes = 100,
            Status = MaterialStatus.Published, CreatedAt = DateTime.UtcNow.AddMinutes(-10)
        };
        var published2 = new Material
        {
            Id = Guid.NewGuid(), SubjectId = 1, Subject = subject, UploadedByUserId = user.Id, UploadedByUser = user,
            Title = "Pub 2", Description = "Desc", MaterialType = MaterialType.LectureNote,
            FileStorageKey = "k2", OriginalFileName = "p2.pdf", ContentType = "application/pdf", FileSizeBytes = 100,
            Status = MaterialStatus.Published, CreatedAt = DateTime.UtcNow.AddMinutes(-5)
        };
        var pending = new Material
        {
            Id = Guid.NewGuid(), SubjectId = 1, Subject = subject, UploadedByUserId = user.Id, UploadedByUser = user,
            Title = "Pending 1", Description = "Desc", MaterialType = MaterialType.Exam,
            FileStorageKey = "k3", OriginalFileName = "pen.pdf", ContentType = "application/pdf", FileSizeBytes = 100,
            Status = MaterialStatus.Pending, CreatedAt = DateTime.UtcNow
        };
        var rejected = new Material
        {
            Id = Guid.NewGuid(), SubjectId = 1, Subject = subject, UploadedByUserId = user.Id, UploadedByUser = user,
            Title = "Rejected 1", Description = "Desc", MaterialType = MaterialType.Exam,
            FileStorageKey = "k4", OriginalFileName = "rej.pdf", ContentType = "application/pdf", FileSizeBytes = 100,
            Status = MaterialStatus.Rejected, RejectionReason = "Motivo", CreatedAt = DateTime.UtcNow
        };

        context.Users.Add(user);
        context.Subjects.Add(subject);
        context.Materials.AddRange(published1, published2, pending, rejected);
        await context.SaveChangesAsync();

        var sut = new MaterialService(context, new TestFileStorageService(), new TestNotificationQueue(), Options.Create(new EmailOptions()), NullLogger<MaterialService>.Instance);

        var result = await sut.GetPublishedMaterialsAsync(new MaterialQueryFilter());

        result.TotalCount.Should().Be(2);
        result.Items.Should().HaveCount(2);
        result.Items.Select(i => i.Id).Should().Contain([published1.Id, published2.Id]);
        result.Items.Select(i => i.Id).Should().NotContain([pending.Id, rejected.Id]);
    }

    [Fact]
    public async Task GetPublishedMaterialsAsync_WithPagination_ShouldSliceCorrectly()
    {
        using var context = CreateDbContext();
        var user = new User { Id = Guid.NewGuid(), Name = "User", Email = "u@isutic.ed.ao", Role = UserRole.Delegado, IsActive = true };
        var subject = new Subject { Id = 1, YearLevel = 1, Code = "IGF101", Name = "Cálculo I" };
        context.Users.Add(user);
        context.Subjects.Add(subject);

        for (int i = 1; i <= 5; i++)
        {
            context.Materials.Add(new Material
            {
                Id = Guid.NewGuid(), SubjectId = 1, Subject = subject, UploadedByUserId = user.Id, UploadedByUser = user,
                Title = $"Material {i}", Description = "Desc", MaterialType = MaterialType.Exam,
                FileStorageKey = $"k{i}", OriginalFileName = $"f{i}.pdf", ContentType = "application/pdf", FileSizeBytes = 100,
                Status = MaterialStatus.Published, CreatedAt = DateTime.UtcNow.AddMinutes(i)
            });
        }
        await context.SaveChangesAsync();

        var sut = new MaterialService(context, new TestFileStorageService(), new TestNotificationQueue(), Options.Create(new EmailOptions()), NullLogger<MaterialService>.Instance);

        var result = await sut.GetPublishedMaterialsAsync(new MaterialQueryFilter(Page: 2, PageSize: 2));

        result.Page.Should().Be(2);
        result.PageSize.Should().Be(2);
        result.TotalCount.Should().Be(5);
        result.TotalPages.Should().Be(3);
        result.Items.Should().HaveCount(2);
    }

    [Fact]
    public async Task GetPublishedMaterialsAsync_WithFilters_ShouldReturnFilteredResults()
    {
        using var context = CreateDbContext();
        var user = new User { Id = Guid.NewGuid(), Name = "User", Email = "u@isutic.ed.ao", Role = UserRole.Delegado, IsActive = true };
        var year1 = new AcademicYear { Id = 1, YearLevel = 1, Name = "1.º Ano" };
        var year2 = new AcademicYear { Id = 2, YearLevel = 2, Name = "2.º Ano" };
        var sub1 = new Subject { Id = 1, YearLevel = 1, AcademicYear = year1, Code = "IGF101", Name = "Cálculo I" };
        var sub2 = new Subject { Id = 2, YearLevel = 2, AcademicYear = year2, Code = "IGF201", Name = "Base de Dados" };

        var m1 = new Material
        {
            Id = Guid.NewGuid(), SubjectId = 1, Subject = sub1, UploadedByUserId = user.Id, UploadedByUser = user,
            Title = "Prova Cálculo", Description = "Desc", MaterialType = MaterialType.Exam,
            FileStorageKey = "k1", OriginalFileName = "f1.pdf", ContentType = "application/pdf", FileSizeBytes = 100,
            Status = MaterialStatus.Published, CreatedAt = DateTime.UtcNow
        };
        var m2 = new Material
        {
            Id = Guid.NewGuid(), SubjectId = 1, Subject = sub1, UploadedByUserId = user.Id, UploadedByUser = user,
            Title = "Sebenta Cálculo", Description = "Desc", MaterialType = MaterialType.LectureNote,
            FileStorageKey = "k2", OriginalFileName = "f2.pdf", ContentType = "application/pdf", FileSizeBytes = 100,
            Status = MaterialStatus.Published, CreatedAt = DateTime.UtcNow
        };
        var m3 = new Material
        {
            Id = Guid.NewGuid(), SubjectId = 2, Subject = sub2, UploadedByUserId = user.Id, UploadedByUser = user,
            Title = "Prova BD", Description = "Desc", MaterialType = MaterialType.Exam,
            FileStorageKey = "k3", OriginalFileName = "f3.pdf", ContentType = "application/pdf", FileSizeBytes = 100,
            Status = MaterialStatus.Published, CreatedAt = DateTime.UtcNow
        };

        context.Users.Add(user);
        context.AcademicYears.AddRange(year1, year2);
        context.Subjects.AddRange(sub1, sub2);
        context.Materials.AddRange(m1, m2, m3);
        await context.SaveChangesAsync();

        var sut = new MaterialService(context, new TestFileStorageService(), new TestNotificationQueue(), Options.Create(new EmailOptions()), NullLogger<MaterialService>.Instance);

        // Filter by YearLevel
        var resYear2 = await sut.GetPublishedMaterialsAsync(new MaterialQueryFilter(YearLevel: 2));
        resYear2.TotalCount.Should().Be(1);
        resYear2.Items[0].Id.Should().Be(m3.Id);

        // Filter by SubjectId
        var resSub1 = await sut.GetPublishedMaterialsAsync(new MaterialQueryFilter(SubjectId: 1));
        resSub1.TotalCount.Should().Be(2);

        // Filter by MaterialType
        var resExam = await sut.GetPublishedMaterialsAsync(new MaterialQueryFilter(MaterialType: MaterialType.Exam));
        resExam.TotalCount.Should().Be(2);
        resExam.Items.Select(i => i.Id).Should().Contain([m1.Id, m3.Id]);
    }

    [Fact]
    public async Task GetMaterialForDownloadAsync_WhenPublished_ShouldReturnStreamAndDetails()
    {
        using var context = CreateDbContext();
        var storage = new TestFileStorageService();
        var user = new User { Id = Guid.NewGuid(), Name = "User", Email = "u@isutic.ed.ao", Role = UserRole.Delegado, IsActive = true };
        var subject = new Subject { Id = 1, YearLevel = 1, Code = "IGF101", Name = "Cálculo I" };
        var key = "keys/pub.pdf";
        storage.StoredFiles[key] = [0x25, 0x50, 0x44, 0x46]; // %PDF

        var material = new Material
        {
            Id = Guid.NewGuid(), SubjectId = 1, Subject = subject, UploadedByUserId = user.Id, UploadedByUser = user,
            Title = "Publicado", Description = "Desc", MaterialType = MaterialType.Exam,
            FileStorageKey = key, OriginalFileName = "original.pdf", ContentType = "application/pdf", FileSizeBytes = 4,
            Status = MaterialStatus.Published, CreatedAt = DateTime.UtcNow
        };

        context.Users.Add(user);
        context.Subjects.Add(subject);
        context.Materials.Add(material);
        await context.SaveChangesAsync();

        var sut = new MaterialService(context, storage, new TestNotificationQueue(), Options.Create(new EmailOptions()), NullLogger<MaterialService>.Instance);

        var result = await sut.GetMaterialForDownloadAsync(material.Id);

        result.Should().NotBeNull();
        result!.ContentType.Should().Be("application/pdf");
        result.OriginalFileName.Should().Be("original.pdf");
        result.Content.Should().NotBeNull();
    }

    [Fact]
    public async Task GetMaterialForDownloadAsync_WhenPendingOrRejected_ShouldReturnNull()
    {
        using var context = CreateDbContext();
        var storage = new TestFileStorageService();
        var user = new User { Id = Guid.NewGuid(), Name = "User", Email = "u@isutic.ed.ao", Role = UserRole.Delegado, IsActive = true };
        var subject = new Subject { Id = 1, YearLevel = 1, Code = "IGF101", Name = "Cálculo I" };

        var pending = new Material
        {
            Id = Guid.NewGuid(), SubjectId = 1, Subject = subject, UploadedByUserId = user.Id, UploadedByUser = user,
            Title = "Pendente", Description = "Desc", MaterialType = MaterialType.Exam,
            FileStorageKey = "k1", OriginalFileName = "f1.pdf", ContentType = "application/pdf", FileSizeBytes = 10,
            Status = MaterialStatus.Pending, CreatedAt = DateTime.UtcNow
        };
        var rejected = new Material
        {
            Id = Guid.NewGuid(), SubjectId = 1, Subject = subject, UploadedByUserId = user.Id, UploadedByUser = user,
            Title = "Rejeitado", Description = "Desc", MaterialType = MaterialType.Exam,
            FileStorageKey = "k2", OriginalFileName = "f2.pdf", ContentType = "application/pdf", FileSizeBytes = 10,
            Status = MaterialStatus.Rejected, CreatedAt = DateTime.UtcNow
        };

        context.Users.Add(user);
        context.Subjects.Add(subject);
        context.Materials.AddRange(pending, rejected);
        await context.SaveChangesAsync();

        var sut = new MaterialService(context, storage, new TestNotificationQueue(), Options.Create(new EmailOptions()), NullLogger<MaterialService>.Instance);

        var resPending = await sut.GetMaterialForDownloadAsync(pending.Id);
        var resRejected = await sut.GetMaterialForDownloadAsync(rejected.Id);

        resPending.Should().BeNull();
        resRejected.Should().BeNull();
    }
}
