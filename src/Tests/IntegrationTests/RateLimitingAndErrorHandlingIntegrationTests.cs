using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using Core.Entities;
using Core.Enums;
using Core.Interfaces;
using FluentAssertions;
using Infrastructure.Data;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;

namespace IntegrationTests;

public class RateLimitingAndErrorHandlingIntegrationTests : IClassFixture<WebApplicationFactory<Program>>
{
    private readonly WebApplicationFactory<Program> _factory;

    public RateLimitingAndErrorHandlingIntegrationTests(WebApplicationFactory<Program> factory)
    {
        _factory = factory;
    }

    private WebApplicationFactory<Program> CreateFactoryWithLimits(int generalLimit, int strictLimit)
    {
        var inMemoryDbName = "IntegrationRateLimiting_" + Guid.NewGuid().ToString("N");

        return _factory.WithWebHostBuilder(builder =>
        {
            builder.UseEnvironment("Testing");

            builder.ConfigureAppConfiguration((_, config) =>
            {
                config.AddInMemoryCollection(new Dictionary<string, string?>
                {
                    ["RateLimiting:GeneralPermitLimit"] = generalLimit.ToString(),
                    ["RateLimiting:GeneralWindowSeconds"] = "60",
                    ["RateLimiting:StrictPermitLimit"] = strictLimit.ToString(),
                    ["RateLimiting:StrictWindowSeconds"] = "60"
                });
            });

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
    }

    private string GenerateToken(WebApplicationFactory<Program> factory, User user)
    {
        using var scope = factory.Services.CreateScope();
        var jwtService = scope.ServiceProvider.GetRequiredService<IJwtTokenService>();
        return jwtService.GenerateAccessToken(user);
    }

    [Fact]
    public async Task RateLimiting_StrictPolicy_WhenExceedingLimit_ShouldReturn429TooManyRequestsWithProblemDetails()
    {
        // Limit strict requests to 3
        var factory = CreateFactoryWithLimits(generalLimit: 100, strictLimit: 3);
        var client = factory.CreateClient();

        var delegado = new User { Id = Guid.NewGuid(), Name = "Delegado Teste", Email = "del@isutic.ed.ao", Role = UserRole.Delegado, IsActive = true };
        using (var scope = factory.Services.CreateScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
            db.Users.Add(delegado);
            db.SaveChanges();
        }

        var token = GenerateToken(factory, delegado);
        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", token);

        // Send 3 requests (within limit)
        for (int i = 0; i < 3; i++)
        {
            using var content = new MultipartFormDataContent();
            content.Add(new StringContent("Título"), "title");
            var res = await client.PostAsync("/api/v1/materials", content);
            // It may fail validation (400), but NOT 429
            res.StatusCode.Should().NotBe(HttpStatusCode.TooManyRequests);
        }

        // 4th request must be rejected by Rate Limiter with 429
        using var blockedContent = new MultipartFormDataContent();
        blockedContent.Add(new StringContent("Título"), "title");
        var response = await client.PostAsync("/api/v1/materials", blockedContent);

        response.StatusCode.Should().Be(HttpStatusCode.TooManyRequests);
        response.Content.Headers.ContentType!.MediaType.Should().Be("application/problem+json");

        var problemDetails = await response.Content.ReadFromJsonAsync<ProblemDetails>();
        problemDetails.Should().NotBeNull();
        problemDetails!.Status.Should().Be(429);
        problemDetails.Title.Should().Be("Too Many Requests");
        problemDetails.Detail.Should().Contain("Limite de requisições excedido");
        response.Headers.Should().ContainKey("Retry-After");
    }

    [Fact]
    public async Task RateLimiting_GeneralPolicy_WhenExceedingLimit_ShouldReturn429TooManyRequests()
    {
        // Limit general requests to 3
        var factory = CreateFactoryWithLimits(generalLimit: 3, strictLimit: 100);
        var client = factory.CreateClient();

        for (int i = 0; i < 3; i++)
        {
            var res = await client.GetAsync("/health");
            res.StatusCode.Should().Be(HttpStatusCode.OK);
        }

        // 4th request exceeds general limit
        var response = await client.GetAsync("/health");

        response.StatusCode.Should().Be(HttpStatusCode.TooManyRequests);
        response.Content.Headers.ContentType!.MediaType.Should().Be("application/problem+json");

        var problemDetails = await response.Content.ReadFromJsonAsync<ProblemDetails>();
        problemDetails.Should().NotBeNull();
        problemDetails!.Status.Should().Be(429);
        problemDetails.Title.Should().Be("Too Many Requests");
    }

    [Fact]
    public async Task GlobalExceptionHandler_WhenValidationExceptionThrown_ShouldReturn400ProblemDetails()
    {
        var factory = CreateFactoryWithLimits(generalLimit: 100, strictLimit: 100);
        var client = factory.CreateClient();

        var response = await client.GetAsync("/api/v1/test/errors/validation");

        response.StatusCode.Should().Be(HttpStatusCode.BadRequest);
        response.Content.Headers.ContentType!.MediaType.Should().Be("application/problem+json");

        var problemDetails = await response.Content.ReadFromJsonAsync<ProblemDetails>();
        problemDetails.Should().NotBeNull();
        problemDetails!.Status.Should().Be(400);
        problemDetails.Title.Should().Be("Erro de Validação");
        problemDetails.Detail.Should().Be("Falha de validação simulada.");
    }

    [Fact]
    public async Task GlobalExceptionHandler_WhenMaterialNotFoundExceptionThrown_ShouldReturn404ProblemDetails()
    {
        var factory = CreateFactoryWithLimits(generalLimit: 100, strictLimit: 100);
        var client = factory.CreateClient();

        var response = await client.GetAsync("/api/v1/test/errors/not-found");

        response.StatusCode.Should().Be(HttpStatusCode.NotFound);
        response.Content.Headers.ContentType!.MediaType.Should().Be("application/problem+json");

        var problemDetails = await response.Content.ReadFromJsonAsync<ProblemDetails>();
        problemDetails.Should().NotBeNull();
        problemDetails!.Status.Should().Be(404);
        problemDetails.Title.Should().Be("Material Não Encontrado");
    }

    [Fact]
    public async Task GlobalExceptionHandler_WhenForbiddenOperationExceptionThrown_ShouldReturn403ProblemDetails()
    {
        var factory = CreateFactoryWithLimits(generalLimit: 100, strictLimit: 100);
        var client = factory.CreateClient();

        var response = await client.GetAsync("/api/v1/test/errors/forbidden");

        response.StatusCode.Should().Be(HttpStatusCode.Forbidden);
        response.Content.Headers.ContentType!.MediaType.Should().Be("application/problem+json");

        var problemDetails = await response.Content.ReadFromJsonAsync<ProblemDetails>();
        problemDetails.Should().NotBeNull();
        problemDetails!.Status.Should().Be(403);
        problemDetails.Title.Should().Be("Operação Proibida");
    }

    [Fact]
    public async Task GlobalExceptionHandler_WhenFileSizeLimitExceededExceptionThrown_ShouldReturn413ProblemDetails()
    {
        var factory = CreateFactoryWithLimits(generalLimit: 100, strictLimit: 100);
        var client = factory.CreateClient();

        var response = await client.GetAsync("/api/v1/test/errors/file-size");

        response.StatusCode.Should().Be(HttpStatusCode.RequestEntityTooLarge);
        response.Content.Headers.ContentType!.MediaType.Should().Be("application/problem+json");

        var problemDetails = await response.Content.ReadFromJsonAsync<ProblemDetails>();
        problemDetails.Should().NotBeNull();
        problemDetails!.Status.Should().Be(413);
        problemDetails.Title.Should().Be("Payload Too Large");
    }

    [Fact]
    public async Task GlobalExceptionHandler_WhenUnhandledExceptionThrown_ShouldReturn500ProblemDetailsWithoutSensitiveLeak()
    {
        var factory = CreateFactoryWithLimits(generalLimit: 100, strictLimit: 100);
        var client = factory.CreateClient();

        var response = await client.GetAsync("/api/v1/test/errors/unhandled");

        response.StatusCode.Should().Be(HttpStatusCode.InternalServerError);
        response.Content.Headers.ContentType!.MediaType.Should().Be("application/problem+json");

        var problemDetails = await response.Content.ReadFromJsonAsync<ProblemDetails>();
        problemDetails.Should().NotBeNull();
        problemDetails!.Status.Should().Be(500);
        problemDetails.Title.Should().Be("Erro Interno do Servidor");
        problemDetails.Detail.Should().Be("Ocorreu um erro interno inesperado no servidor.");
    }
}
