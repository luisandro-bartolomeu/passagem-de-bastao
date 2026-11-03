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

public class AdminMaterialModerationIntegrationTests : IClassFixture<WebApplicationFactory<Program>>
{
    private readonly WebApplicationFactory<Program> _factory;

    public AdminMaterialModerationIntegrationTests(WebApplicationFactory<Program> factory)
    {
        _factory = factory;
    }

    private class TestEnv
    {
        public required WebApplicationFactory<Program> AppFactory { get; init; }
        public required User Admin { get; init; }
        public required User Delegado { get; init; }
        public required User Estudante { get; init; }
        public required Material PendingMaterial { get; init; }
    }

    private TestEnv CreateEnvironment()
    {
        var inMemoryDbName = "IntegrationModeration_" + Guid.NewGuid().ToString("N");

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
        var subject = new Subject { Id = 1, YearLevel = 1, AcademicYear = year, Code = "IGF101", Name = "Programação I" };

        var admin = new User { Id = Guid.NewGuid(), Name = "Administrador", Email = "admin@isutic.ed.ao", Role = UserRole.Admin, IsActive = true };
        var delegado = new User { Id = Guid.NewGuid(), Name = "Delegado Silva", Email = "delegado@isutic.ed.ao", Role = UserRole.Delegado, IsActive = true };
        var estudante = new User { Id = Guid.NewGuid(), Name = "Estudante Santos", Email = "estudante@isutic.ed.ao", Role = UserRole.Estudante, IsActive = true };

        var pendingMaterial = new Material
        {
            Id = Guid.NewGuid(),
            SubjectId = 1,
            Subject = subject,
            UploadedByUserId = delegado.Id,
            UploadedByUser = delegado,
            Title = "Sebenta de Programação I",
            Description = "Apontamentos teóricos das aulas",
            MaterialType = MaterialType.LectureNote,
            FileStorageKey = "keys/sebenta_prog1.pdf",
            OriginalFileName = "sebenta_prog1.pdf",
            ContentType = "application/pdf",
            FileSizeBytes = 15000,
            Status = MaterialStatus.Pending,
            CreatedAt = DateTime.UtcNow
        };

        db.AcademicYears.Add(year);
        db.Subjects.Add(subject);
        db.Users.AddRange(admin, delegado, estudante);
        db.Materials.Add(pendingMaterial);
        db.SaveChanges();

        return new TestEnv
        {
            AppFactory = appFactory,
            Admin = admin,
            Delegado = delegado,
            Estudante = estudante,
            PendingMaterial = pendingMaterial
        };
    }

    private string GenerateToken(WebApplicationFactory<Program> factory, User user)
    {
        using var scope = factory.Services.CreateScope();
        var jwtService = scope.ServiceProvider.GetRequiredService<IJwtTokenService>();
        return jwtService.GenerateAccessToken(user);
    }

    [Fact]
    public async Task PatchStatus_AsAdmin_ApproveMaterial_ShouldReturn200AndPublishedStatus()
    {
        var env = CreateEnvironment();
        var client = env.AppFactory.CreateClient();
        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", GenerateToken(env.AppFactory, env.Admin));

        var request = new UpdateMaterialStatusRequest(
            Status: "Published",
            RejectionReason: null
        );

        var response = await client.PatchAsJsonAsync($"/api/v1/admin/materials/{env.PendingMaterial.Id}/status", request);

        response.StatusCode.Should().Be(HttpStatusCode.OK);
        var body = await response.Content.ReadFromJsonAsync<MaterialResponseDto>();
        body.Should().NotBeNull();
        body!.Status.Should().Be("Published");
        body.RejectionReason.Should().BeNull();
        body.UpdatedAt.Should().NotBeNull();
    }

    [Fact]
    public async Task PatchStatus_AsAdmin_RejectMaterialWithReason_ShouldReturn200AndRejectedStatus()
    {
        var env = CreateEnvironment();
        var client = env.AppFactory.CreateClient();
        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", GenerateToken(env.AppFactory, env.Admin));

        var rejectionReason = "Documento ilegível e em baixa resolução.";
        var request = new UpdateMaterialStatusRequest(
            Status: "Rejected",
            RejectionReason: rejectionReason
        );

        var response = await client.PatchAsJsonAsync($"/api/v1/admin/materials/{env.PendingMaterial.Id}/status", request);

        response.StatusCode.Should().Be(HttpStatusCode.OK);
        var body = await response.Content.ReadFromJsonAsync<MaterialResponseDto>();
        body.Should().NotBeNull();
        body!.Status.Should().Be("Rejected");
        body.RejectionReason.Should().Be(rejectionReason);
        body.UpdatedAt.Should().NotBeNull();
    }

    [Fact]
    public async Task PatchStatus_AsAdmin_RejectWithoutReason_ShouldReturn400BadRequest()
    {
        var env = CreateEnvironment();
        var client = env.AppFactory.CreateClient();
        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", GenerateToken(env.AppFactory, env.Admin));

        var request = new UpdateMaterialStatusRequest(
            Status: "Rejected",
            RejectionReason: "   " // Empty/whitespace reason
        );

        var response = await client.PatchAsJsonAsync($"/api/v1/admin/materials/{env.PendingMaterial.Id}/status", request);

        response.StatusCode.Should().Be(HttpStatusCode.BadRequest);
        var content = await response.Content.ReadAsStringAsync();
        content.Should().Contain("motivo de rejeição");
    }

    [Fact]
    public async Task PatchStatus_AsAdmin_WithInvalidStatus_ShouldReturn400BadRequest()
    {
        var env = CreateEnvironment();
        var client = env.AppFactory.CreateClient();
        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", GenerateToken(env.AppFactory, env.Admin));

        var request = new UpdateMaterialStatusRequest(
            Status: "Pending", // Pending is not an allowed moderation target
            RejectionReason: null
        );

        var response = await client.PatchAsJsonAsync($"/api/v1/admin/materials/{env.PendingMaterial.Id}/status", request);

        response.StatusCode.Should().Be(HttpStatusCode.BadRequest);
        var content = await response.Content.ReadAsStringAsync();
        content.Should().Contain("Published");
    }

    [Fact]
    public async Task PatchStatus_AsDelegate_ShouldReturn403Forbidden()
    {
        var env = CreateEnvironment();
        var client = env.AppFactory.CreateClient();
        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", GenerateToken(env.AppFactory, env.Delegado));

        var request = new UpdateMaterialStatusRequest("Published", null);
        var response = await client.PatchAsJsonAsync($"/api/v1/admin/materials/{env.PendingMaterial.Id}/status", request);

        response.StatusCode.Should().Be(HttpStatusCode.Forbidden);
    }

    [Fact]
    public async Task PatchStatus_AsStudent_ShouldReturn403Forbidden()
    {
        var env = CreateEnvironment();
        var client = env.AppFactory.CreateClient();
        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", GenerateToken(env.AppFactory, env.Estudante));

        var request = new UpdateMaterialStatusRequest("Published", null);
        var response = await client.PatchAsJsonAsync($"/api/v1/admin/materials/{env.PendingMaterial.Id}/status", request);

        response.StatusCode.Should().Be(HttpStatusCode.Forbidden);
    }

    [Fact]
    public async Task PatchStatus_WithoutAuth_ShouldReturn401Unauthorized()
    {
        var env = CreateEnvironment();
        var client = env.AppFactory.CreateClient();

        var request = new UpdateMaterialStatusRequest("Published", null);
        var response = await client.PatchAsJsonAsync($"/api/v1/admin/materials/{env.PendingMaterial.Id}/status", request);

        response.StatusCode.Should().Be(HttpStatusCode.Unauthorized);
    }

    [Fact]
    public async Task PatchStatus_WithNonExistentMaterial_ShouldReturn404NotFound()
    {
        var env = CreateEnvironment();
        var client = env.AppFactory.CreateClient();
        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", GenerateToken(env.AppFactory, env.Admin));

        var nonExistentId = Guid.NewGuid();
        var request = new UpdateMaterialStatusRequest("Published", null);
        var response = await client.PatchAsJsonAsync($"/api/v1/admin/materials/{nonExistentId}/status", request);

        response.StatusCode.Should().Be(HttpStatusCode.NotFound);
    }
}
