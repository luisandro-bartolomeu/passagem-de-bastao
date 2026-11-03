using System.Net;
using System.Net.Http.Json;
using Core.DTOs;
using Core.Entities;
using Core.Enums;
using Core.Interfaces;
using FluentAssertions;
using Infrastructure.Data;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;

namespace IntegrationTests;

public class PublicCatalogAndDownloadIntegrationTests : IClassFixture<WebApplicationFactory<Program>>
{
    private readonly WebApplicationFactory<Program> _factory;

    private static readonly byte[] ValidPdfBytes =
    [
        0x25, 0x50, 0x44, 0x46, 0x2D, 0x31, 0x2E, 0x35, // %PDF-1.5
        0x25, 0xE2, 0xE3, 0xCF, 0xD3, 0x0A,             // Sample body
        0x25, 0x25, 0x45, 0x4F, 0x46                    // %%EOF
    ];

    public PublicCatalogAndDownloadIntegrationTests(WebApplicationFactory<Program> factory)
    {
        _factory = factory;
    }

    private class TestEnv
    {
        public required WebApplicationFactory<Program> AppFactory { get; init; }
        public required AcademicYear Year1 { get; init; }
        public required AcademicYear Year2 { get; init; }
        public required Subject Subject1 { get; init; }
        public required Subject Subject2 { get; init; }
        public required Material PublishedMaterial1 { get; init; }
        public required Material PublishedMaterial2 { get; init; }
        public required Material PendingMaterial { get; init; }
        public required Material RejectedMaterial { get; init; }
    }

    private async Task<TestEnv> CreateEnvironmentAsync()
    {
        var inMemoryDbName = "IntegrationCatalog_" + Guid.NewGuid().ToString("N");

        var appFactory = _factory.WithWebHostBuilder(builder =>
        {
            builder.ConfigureServices(services =>
            {
                var descriptor = services.SingleOrDefault(d => d.ServiceType == typeof(DbContextOptions<AppDbContext>));
                if (descriptor != null)
                {
                    services.Remove(descriptor);
                }

                var internalServiceProvider = new ServiceCollection()
                    .AddEntityFrameworkInMemoryDatabase()
                    .BuildServiceProvider();

                services.AddDbContext<AppDbContext>(options =>
                {
                    options.UseInMemoryDatabase(inMemoryDbName);
                    options.UseInternalServiceProvider(internalServiceProvider);
                });
            });
        });

        using var scope = appFactory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        var storage = scope.ServiceProvider.GetRequiredService<IFileStorageService>();

        // Upload physical files for testing download
        using var stream1 = new MemoryStream(ValidPdfBytes);
        var upload1 = await storage.UploadAsync(stream1, "pub1.pdf", "application/pdf");

        using var stream2 = new MemoryStream(ValidPdfBytes);
        var upload2 = await storage.UploadAsync(stream2, "pub2.pdf", "application/pdf");

        using var streamPending = new MemoryStream(ValidPdfBytes);
        var uploadPending = await storage.UploadAsync(streamPending, "pen.pdf", "application/pdf");

        using var streamRejected = new MemoryStream(ValidPdfBytes);
        var uploadRejected = await storage.UploadAsync(streamRejected, "rej.pdf", "application/pdf");

        var year1 = new AcademicYear { Id = 1, YearLevel = 1, Name = "1.º Ano" };
        var year2 = new AcademicYear { Id = 2, YearLevel = 2, Name = "2.º Ano" };
        var subject1 = new Subject { Id = 1, YearLevel = 1, AcademicYear = year1, Code = "IGF101", Name = "Programação I" };
        var subject2 = new Subject { Id = 2, YearLevel = 2, AcademicYear = year2, Code = "IGF201", Name = "Base de Dados" };

        var user = new User { Id = Guid.NewGuid(), Name = "Delegado Silva", Email = "del@isutic.ed.ao", Role = UserRole.Delegado, IsActive = true };

        var published1 = new Material
        {
            Id = Guid.NewGuid(),
            SubjectId = 1,
            Subject = subject1,
            UploadedByUserId = user.Id,
            UploadedByUser = user,
            Title = "Exame 2025 Programação I",
            Description = "Enunciado oficial de recurso",
            MaterialType = MaterialType.Exam,
            FileStorageKey = upload1.StorageKey,
            OriginalFileName = "exame_prog1_2025.pdf",
            ContentType = "application/pdf",
            FileSizeBytes = ValidPdfBytes.Length,
            Status = MaterialStatus.Published,
            CreatedAt = DateTime.UtcNow.AddHours(-3)
        };

        var published2 = new Material
        {
            Id = Guid.NewGuid(),
            SubjectId = 2,
            Subject = subject2,
            UploadedByUserId = user.Id,
            UploadedByUser = user,
            Title = "Sebenta de SQL",
            Description = "Apontamentos teóricos e exercícios",
            MaterialType = MaterialType.LectureNote,
            FileStorageKey = upload2.StorageKey,
            OriginalFileName = "sebenta_sql.pdf",
            ContentType = "application/pdf",
            FileSizeBytes = ValidPdfBytes.Length,
            Status = MaterialStatus.Published,
            CreatedAt = DateTime.UtcNow.AddHours(-2)
        };

        var pending = new Material
        {
            Id = Guid.NewGuid(),
            SubjectId = 1,
            Subject = subject1,
            UploadedByUserId = user.Id,
            UploadedByUser = user,
            Title = "Material Rascunho",
            Description = "Ainda não moderado",
            MaterialType = MaterialType.Exam,
            FileStorageKey = uploadPending.StorageKey,
            OriginalFileName = "rascunho.pdf",
            ContentType = "application/pdf",
            FileSizeBytes = ValidPdfBytes.Length,
            Status = MaterialStatus.Pending,
            CreatedAt = DateTime.UtcNow
        };

        var rejected = new Material
        {
            Id = Guid.NewGuid(),
            SubjectId = 1,
            Subject = subject1,
            UploadedByUserId = user.Id,
            UploadedByUser = user,
            Title = "Material Recusado",
            Description = "Recusado por rasuras",
            MaterialType = MaterialType.Exam,
            FileStorageKey = uploadRejected.StorageKey,
            OriginalFileName = "recusado.pdf",
            ContentType = "application/pdf",
            FileSizeBytes = ValidPdfBytes.Length,
            Status = MaterialStatus.Rejected,
            RejectionReason = "Ilegível",
            CreatedAt = DateTime.UtcNow
        };

        db.AcademicYears.AddRange(year1, year2);
        db.Subjects.AddRange(subject1, subject2);
        db.Users.Add(user);
        db.Materials.AddRange(published1, published2, pending, rejected);
        await db.SaveChangesAsync();

        return new TestEnv
        {
            AppFactory = appFactory,
            Year1 = year1,
            Year2 = year2,
            Subject1 = subject1,
            Subject2 = subject2,
            PublishedMaterial1 = published1,
            PublishedMaterial2 = published2,
            PendingMaterial = pending,
            RejectedMaterial = rejected
        };
    }

    [Fact]
    public async Task GetMaterials_DefaultQuery_ShouldOnlyReturnPublishedMaterialsInEnvelope()
    {
        var env = await CreateEnvironmentAsync();
        var client = env.AppFactory.CreateClient();

        var response = await client.GetAsync("/api/v1/materials");

        response.StatusCode.Should().Be(HttpStatusCode.OK);
        var body = await response.Content.ReadFromJsonAsync<PagedResult<MaterialResponseDto>>();
        body.Should().NotBeNull();
        body!.TotalCount.Should().Be(2);
        body.Page.Should().Be(1);
        body.PageSize.Should().Be(20);
        body.TotalPages.Should().Be(1);
        body.Items.Should().HaveCount(2);

        // Crucial Catalog Isolation: Must not contain pending or rejected items
        body.Items.Select(m => m.Id).Should().Contain([env.PublishedMaterial1.Id, env.PublishedMaterial2.Id]);
        body.Items.Select(m => m.Id).Should().NotContain([env.PendingMaterial.Id, env.RejectedMaterial.Id]);
    }

    [Fact]
    public async Task GetMaterials_WithPagination_ShouldRespectPageAndPageSize()
    {
        var env = await CreateEnvironmentAsync();
        var client = env.AppFactory.CreateClient();

        var response = await client.GetAsync("/api/v1/materials?page=1&pageSize=1");

        response.StatusCode.Should().Be(HttpStatusCode.OK);
        var body = await response.Content.ReadFromJsonAsync<PagedResult<MaterialResponseDto>>();
        body.Should().NotBeNull();
        body!.Page.Should().Be(1);
        body.PageSize.Should().Be(1);
        body.TotalCount.Should().Be(2);
        body.TotalPages.Should().Be(2);
        body.Items.Should().HaveCount(1);
    }

    [Fact]
    public async Task GetMaterials_WithOversizedPageSize_ShouldClampToStrictLimit50()
    {
        var env = await CreateEnvironmentAsync();
        var client = env.AppFactory.CreateClient();

        var response = await client.GetAsync("/api/v1/materials?pageSize=100");

        response.StatusCode.Should().Be(HttpStatusCode.OK);
        var body = await response.Content.ReadFromJsonAsync<PagedResult<MaterialResponseDto>>();
        body.Should().NotBeNull();
        body!.PageSize.Should().Be(50);
    }

    [Fact]
    public async Task GetMaterials_FilterByYearLevel_ShouldReturnOnlyMatchingYear()
    {
        var env = await CreateEnvironmentAsync();
        var client = env.AppFactory.CreateClient();

        var response = await client.GetAsync("/api/v1/materials?yearLevel=2");

        response.StatusCode.Should().Be(HttpStatusCode.OK);
        var body = await response.Content.ReadFromJsonAsync<PagedResult<MaterialResponseDto>>();
        body.Should().NotBeNull();
        body!.TotalCount.Should().Be(1);
        body.Items[0].Id.Should().Be(env.PublishedMaterial2.Id);
    }

    [Fact]
    public async Task GetMaterials_FilterBySubjectId_ShouldReturnOnlyMatchingSubject()
    {
        var env = await CreateEnvironmentAsync();
        var client = env.AppFactory.CreateClient();

        var response = await client.GetAsync($"/api/v1/materials?subjectId={env.Subject1.Id}");

        response.StatusCode.Should().Be(HttpStatusCode.OK);
        var body = await response.Content.ReadFromJsonAsync<PagedResult<MaterialResponseDto>>();
        body.Should().NotBeNull();
        body!.TotalCount.Should().Be(1);
        body.Items[0].Id.Should().Be(env.PublishedMaterial1.Id);
    }

    [Fact]
    public async Task GetMaterials_FilterByMaterialType_ShouldReturnOnlyMatchingType()
    {
        var env = await CreateEnvironmentAsync();
        var client = env.AppFactory.CreateClient();

        var response = await client.GetAsync("/api/v1/materials?materialType=LectureNote");

        response.StatusCode.Should().Be(HttpStatusCode.OK);
        var body = await response.Content.ReadFromJsonAsync<PagedResult<MaterialResponseDto>>();
        body.Should().NotBeNull();
        body!.TotalCount.Should().Be(1);
        body.Items[0].Id.Should().Be(env.PublishedMaterial2.Id);
    }

    [Fact]
    public async Task GetMaterials_WithInvalidYearLevel_ShouldReturn400BadRequest()
    {
        var env = await CreateEnvironmentAsync();
        var client = env.AppFactory.CreateClient();

        var response = await client.GetAsync("/api/v1/materials?yearLevel=5");

        response.StatusCode.Should().Be(HttpStatusCode.BadRequest);
        var content = await response.Content.ReadAsStringAsync();
        content.Should().Contain("Ano Curricular Inválido");
    }

    [Fact]
    public async Task GetMaterials_WithInvalidMaterialType_ShouldReturn400BadRequest()
    {
        var env = await CreateEnvironmentAsync();
        var client = env.AppFactory.CreateClient();

        var response = await client.GetAsync("/api/v1/materials?materialType=FormatoDesconhecido");

        response.StatusCode.Should().Be(HttpStatusCode.BadRequest);
        var content = await response.Content.ReadAsStringAsync();
        content.Should().Contain("Tipo de Material Inválido");
    }

    [Fact]
    public async Task DownloadMaterial_WhenPublished_ShouldReturn200WithStreamAndHeaders()
    {
        var env = await CreateEnvironmentAsync();
        var client = env.AppFactory.CreateClient();

        var response = await client.GetAsync($"/api/v1/materials/{env.PublishedMaterial1.Id}/download");

        response.StatusCode.Should().Be(HttpStatusCode.OK);
        response.Content.Headers.ContentType!.MediaType.Should().Be("application/pdf");
        response.Content.Headers.ContentDisposition.Should().NotBeNull();
        response.Content.Headers.ContentDisposition!.FileName.Should().Contain(env.PublishedMaterial1.OriginalFileName);

        var downloadedBytes = await response.Content.ReadAsByteArrayAsync();
        downloadedBytes.Should().Equal(ValidPdfBytes);
    }

    [Fact]
    public async Task DownloadMaterial_WhenPending_ShouldReturn404NotFound()
    {
        var env = await CreateEnvironmentAsync();
        var client = env.AppFactory.CreateClient();

        var response = await client.GetAsync($"/api/v1/materials/{env.PendingMaterial.Id}/download");

        response.StatusCode.Should().Be(HttpStatusCode.NotFound);
    }

    [Fact]
    public async Task DownloadMaterial_WhenRejected_ShouldReturn404NotFound()
    {
        var env = await CreateEnvironmentAsync();
        var client = env.AppFactory.CreateClient();

        var response = await client.GetAsync($"/api/v1/materials/{env.RejectedMaterial.Id}/download");

        response.StatusCode.Should().Be(HttpStatusCode.NotFound);
    }

    [Fact]
    public async Task DownloadMaterial_WhenNonExistent_ShouldReturn404NotFound()
    {
        var env = await CreateEnvironmentAsync();
        var client = env.AppFactory.CreateClient();

        var response = await client.GetAsync($"/api/v1/materials/{Guid.NewGuid()}/download");

        response.StatusCode.Should().Be(HttpStatusCode.NotFound);
    }
}
