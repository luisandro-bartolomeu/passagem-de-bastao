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
            return Task.FromResult(new FileStorageResult(key, fileName, contentType, 1024));
        }

        public Task<FileDownloadResult?> DownloadAsync(string storageKey, CancellationToken cancellationToken = default) =>
            Task.FromResult<FileDownloadResult?>(null);

        public Task<bool> DeleteAsync(string storageKey, CancellationToken cancellationToken = default)
        {
            DeletedKeys.Add(storageKey);
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
}
