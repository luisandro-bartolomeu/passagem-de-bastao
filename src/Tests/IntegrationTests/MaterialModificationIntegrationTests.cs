using System.Net;
using System.Net.Http.Headers;
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

public class MaterialModificationIntegrationTests : IClassFixture<WebApplicationFactory<Program>>
{
    private readonly WebApplicationFactory<Program> _factory;

    public MaterialModificationIntegrationTests(WebApplicationFactory<Program> factory)
    {
        _factory = factory;
    }

    private class TestEnv
    {
        public required WebApplicationFactory<Program> AppFactory { get; init; }
        public required User Admin { get; init; }
        public required User Delegado1 { get; init; }
        public required User Delegado2 { get; init; }
        public required User Estudante { get; init; }
        public required Subject Subject1 { get; init; }
        public required Subject Subject2 { get; init; }
        public required Material PendingMaterial { get; init; }
        public required Material PublishedMaterial { get; init; }
    }

    private TestEnv CreateEnvironment()
    {
        var inMemoryDbName = "IntegrationModification_" + Guid.NewGuid().ToString("N");

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

        var year = new AcademicYear { Id = 1, YearLevel = 1, Name = "1.º Ano" };
        var subject1 = new Subject { Id = 1, YearLevel = 1, AcademicYear = year, Code = "IGF101", Name = "Programação I" };
        var subject2 = new Subject { Id = 2, YearLevel = 1, AcademicYear = year, Code = "IGF102", Name = "Cálculo I" };

        var admin = new User { Id = Guid.NewGuid(), Name = "Administrador Geral", Email = "admin@isutic.ed.ao", Role = UserRole.Admin, IsActive = true };
        var delegado1 = new User { Id = Guid.NewGuid(), Name = "Delegado Turma A", Email = "del1@isutic.ed.ao", Role = UserRole.Delegado, IsActive = true };
        var delegado2 = new User { Id = Guid.NewGuid(), Name = "Delegado Turma B", Email = "del2@isutic.ed.ao", Role = UserRole.Delegado, IsActive = true };
        var estudante = new User { Id = Guid.NewGuid(), Name = "Estudante Comum", Email = "est@isutic.ed.ao", Role = UserRole.Estudante, IsActive = true };

        var pendingMaterial = new Material
        {
            Id = Guid.NewGuid(),
            SubjectId = 1,
            Subject = subject1,
            UploadedByUserId = delegado1.Id,
            UploadedByUser = delegado1,
            Title = "Enunciado Prova 1",
            Description = "Prova do primeiro semestre",
            MaterialType = MaterialType.Exam,
            FileStorageKey = "keys/pending_prova1.pdf",
            OriginalFileName = "prova1.pdf",
            ContentType = "application/pdf",
            FileSizeBytes = 12040,
            Status = MaterialStatus.Pending,
            CreatedAt = DateTime.UtcNow.AddHours(-2)
        };

        var publishedMaterial = new Material
        {
            Id = Guid.NewGuid(),
            SubjectId = 1,
            Subject = subject1,
            UploadedByUserId = delegado1.Id,
            UploadedByUser = delegado1,
            Title = "Sebenta Completa",
            Description = "Sebenta aprovada e publicada",
            MaterialType = MaterialType.LectureNote,
            FileStorageKey = "keys/published_sebenta.pdf",
            OriginalFileName = "sebenta.pdf",
            ContentType = "application/pdf",
            FileSizeBytes = 25000,
            Status = MaterialStatus.Published,
            CreatedAt = DateTime.UtcNow.AddDays(-1)
        };

        db.AcademicYears.Add(year);
        db.Subjects.AddRange(subject1, subject2);
        db.Users.AddRange(admin, delegado1, delegado2, estudante);
        db.Materials.AddRange(pendingMaterial, publishedMaterial);
        db.SaveChanges();

        return new TestEnv
        {
            AppFactory = appFactory,
            Admin = admin,
            Delegado1 = delegado1,
            Delegado2 = delegado2,
            Estudante = estudante,
            Subject1 = subject1,
            Subject2 = subject2,
            PendingMaterial = pendingMaterial,
            PublishedMaterial = publishedMaterial
        };
    }

    private string GenerateToken(WebApplicationFactory<Program> factory, User user)
    {
        using var scope = factory.Services.CreateScope();
        var jwtService = scope.ServiceProvider.GetRequiredService<IJwtTokenService>();
        return jwtService.GenerateAccessToken(user);
    }

    [Fact]
    public async Task PutMaterial_AsOwnerDelegado_WhenPending_ShouldReturn200AndUpdatedData()
    {
        var env = CreateEnvironment();
        var client = env.AppFactory.CreateClient();
        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", GenerateToken(env.AppFactory, env.Delegado1));

        var request = new UpdateMaterialRequest(
            Title: "Enunciado Prova 1 (Atualizado)",
            Description: "Descrição detalhada atualizada",
            MaterialType: "Exam",
            SubjectId: env.Subject2.Id,
            PeerHelperName: "Monitor João",
            PeerHelperContact: "joao@isutic.ed.ao"
        );

        var response = await client.PutAsJsonAsync($"/api/v1/materials/{env.PendingMaterial.Id}", request);

        response.StatusCode.Should().Be(HttpStatusCode.OK);
        var body = await response.Content.ReadFromJsonAsync<MaterialResponseDto>();
        body.Should().NotBeNull();
        body!.Title.Should().Be("Enunciado Prova 1 (Atualizado)");
        body.Description.Should().Be("Descrição detalhada atualizada");
        body.SubjectId.Should().Be(env.Subject2.Id);
        body.SubjectName.Should().Be(env.Subject2.Name);
        body.PeerHelperName.Should().Be("Monitor João");
        body.UpdatedAt.Should().NotBeNull();
    }

    [Fact]
    public async Task DeleteMaterial_AsOwnerDelegado_WhenPending_ShouldReturn204NoContentAndRemoveRecord()
    {
        var env = CreateEnvironment();
        var client = env.AppFactory.CreateClient();
        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", GenerateToken(env.AppFactory, env.Delegado1));

        var deleteResponse = await client.DeleteAsync($"/api/v1/materials/{env.PendingMaterial.Id}");

        deleteResponse.StatusCode.Should().Be(HttpStatusCode.NoContent);

        // Verification: subsequent GET should return 404 Not Found
        var getResponse = await client.GetAsync($"/api/v1/materials/{env.PendingMaterial.Id}");
        getResponse.StatusCode.Should().Be(HttpStatusCode.NotFound);
    }

    [Fact]
    public async Task PutMaterial_AsOwnerDelegado_WhenPublished_ShouldReturn403ForbiddenWithProblemDetails()
    {
        var env = CreateEnvironment();
        var client = env.AppFactory.CreateClient();
        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", GenerateToken(env.AppFactory, env.Delegado1));

        var request = new UpdateMaterialRequest(
            Title: "Tentativa de Alteração",
            Description: "Tentativa em material já aprovado",
            MaterialType: "LectureNote",
            SubjectId: env.Subject1.Id,
            PeerHelperName: null,
            PeerHelperContact: null
        );

        var response = await client.PutAsJsonAsync($"/api/v1/materials/{env.PublishedMaterial.Id}", request);

        response.StatusCode.Should().Be(HttpStatusCode.Forbidden);
        var content = await response.Content.ReadAsStringAsync();
        content.Should().Contain("não podem ser alterados por delegados");
    }

    [Fact]
    public async Task DeleteMaterial_AsOwnerDelegado_WhenPublished_ShouldReturn403ForbiddenWithProblemDetails()
    {
        var env = CreateEnvironment();
        var client = env.AppFactory.CreateClient();
        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", GenerateToken(env.AppFactory, env.Delegado1));

        var response = await client.DeleteAsync($"/api/v1/materials/{env.PublishedMaterial.Id}");

        response.StatusCode.Should().Be(HttpStatusCode.Forbidden);
        var content = await response.Content.ReadAsStringAsync();
        content.Should().Contain("não podem ser eliminados por delegados");
    }

    [Fact]
    public async Task PutMaterial_AsAnotherDelegado_ShouldReturn403Forbidden()
    {
        var env = CreateEnvironment();
        var client = env.AppFactory.CreateClient();
        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", GenerateToken(env.AppFactory, env.Delegado2));

        var request = new UpdateMaterialRequest(
            Title: "Alteração Ilegítima",
            Description: "Tentativa de modificar material de colega",
            MaterialType: "Exam",
            SubjectId: env.Subject1.Id,
            PeerHelperName: null,
            PeerHelperContact: null
        );

        var response = await client.PutAsJsonAsync($"/api/v1/materials/{env.PendingMaterial.Id}", request);

        response.StatusCode.Should().Be(HttpStatusCode.Forbidden);
        var content = await response.Content.ReadAsStringAsync();
        content.Should().Contain("submetidos por outros utilizadores");
    }

    [Fact]
    public async Task DeleteMaterial_AsAnotherDelegado_ShouldReturn403Forbidden()
    {
        var env = CreateEnvironment();
        var client = env.AppFactory.CreateClient();
        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", GenerateToken(env.AppFactory, env.Delegado2));

        var response = await client.DeleteAsync($"/api/v1/materials/{env.PendingMaterial.Id}");

        response.StatusCode.Should().Be(HttpStatusCode.Forbidden);
        var content = await response.Content.ReadAsStringAsync();
        content.Should().Contain("submetidos por outros utilizadores");
    }

    [Fact]
    public async Task PutMaterial_AsAdmin_WhenPublished_ShouldReturn200OK()
    {
        var env = CreateEnvironment();
        var client = env.AppFactory.CreateClient();
        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", GenerateToken(env.AppFactory, env.Admin));

        var request = new UpdateMaterialRequest(
            Title: "Sebenta Revista pelo Administrador",
            Description: "Conteúdo editado diretamente pelo Admin",
            MaterialType: "LectureNote",
            SubjectId: env.Subject1.Id,
            PeerHelperName: null,
            PeerHelperContact: null
        );

        var response = await client.PutAsJsonAsync($"/api/v1/materials/{env.PublishedMaterial.Id}", request);

        response.StatusCode.Should().Be(HttpStatusCode.OK);
        var body = await response.Content.ReadFromJsonAsync<MaterialResponseDto>();
        body.Should().NotBeNull();
        body!.Title.Should().Be("Sebenta Revista pelo Administrador");
    }

    [Fact]
    public async Task DeleteMaterial_AsAdmin_WhenPublished_ShouldReturn204NoContent()
    {
        var env = CreateEnvironment();
        var client = env.AppFactory.CreateClient();
        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", GenerateToken(env.AppFactory, env.Admin));

        var response = await client.DeleteAsync($"/api/v1/materials/{env.PublishedMaterial.Id}");

        response.StatusCode.Should().Be(HttpStatusCode.NoContent);

        var getResponse = await client.GetAsync($"/api/v1/materials/{env.PublishedMaterial.Id}");
        getResponse.StatusCode.Should().Be(HttpStatusCode.NotFound);
    }

    [Fact]
    public async Task PutMaterial_WithNonExistentId_ShouldReturn404NotFound()
    {
        var env = CreateEnvironment();
        var client = env.AppFactory.CreateClient();
        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", GenerateToken(env.AppFactory, env.Admin));

        var nonExistentId = Guid.NewGuid();
        var request = new UpdateMaterialRequest("Título", "Descrição", "Exam", env.Subject1.Id, null, null);

        var response = await client.PutAsJsonAsync($"/api/v1/materials/{nonExistentId}", request);

        response.StatusCode.Should().Be(HttpStatusCode.NotFound);
    }

    [Fact]
    public async Task DeleteMaterial_WithNonExistentId_ShouldReturn404NotFound()
    {
        var env = CreateEnvironment();
        var client = env.AppFactory.CreateClient();
        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", GenerateToken(env.AppFactory, env.Admin));

        var nonExistentId = Guid.NewGuid();
        var response = await client.DeleteAsync($"/api/v1/materials/{nonExistentId}");

        response.StatusCode.Should().Be(HttpStatusCode.NotFound);
    }

    [Fact]
    public async Task PutMaterial_AsStudent_ShouldReturn403Forbidden()
    {
        var env = CreateEnvironment();
        var client = env.AppFactory.CreateClient();
        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", GenerateToken(env.AppFactory, env.Estudante));

        var request = new UpdateMaterialRequest("Título", "Descrição", "Exam", env.Subject1.Id, null, null);
        var response = await client.PutAsJsonAsync($"/api/v1/materials/{env.PendingMaterial.Id}", request);

        response.StatusCode.Should().Be(HttpStatusCode.Forbidden);
    }

    [Fact]
    public async Task DeleteMaterial_AsStudent_ShouldReturn403Forbidden()
    {
        var env = CreateEnvironment();
        var client = env.AppFactory.CreateClient();
        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", GenerateToken(env.AppFactory, env.Estudante));

        var response = await client.DeleteAsync($"/api/v1/materials/{env.PendingMaterial.Id}");

        response.StatusCode.Should().Be(HttpStatusCode.Forbidden);
    }

    [Fact]
    public async Task PutMaterial_WithoutAuth_ShouldReturn401Unauthorized()
    {
        var env = CreateEnvironment();
        var client = env.AppFactory.CreateClient();

        var request = new UpdateMaterialRequest("Título", "Descrição", "Exam", env.Subject1.Id, null, null);
        var response = await client.PutAsJsonAsync($"/api/v1/materials/{env.PendingMaterial.Id}", request);

        response.StatusCode.Should().Be(HttpStatusCode.Unauthorized);
    }

    [Fact]
    public async Task DeleteMaterial_WithoutAuth_ShouldReturn401Unauthorized()
    {
        var env = CreateEnvironment();
        var client = env.AppFactory.CreateClient();

        var response = await client.DeleteAsync($"/api/v1/materials/{env.PendingMaterial.Id}");

        response.StatusCode.Should().Be(HttpStatusCode.Unauthorized);
    }
}
