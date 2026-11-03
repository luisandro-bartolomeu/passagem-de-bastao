using System.Text;
using Api.Endpoints;
using Api.Middlewares;
using Core.Exceptions;
using Infrastructure;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.AspNetCore.Http.Features;
using Microsoft.IdentityModel.Tokens;

var builder = WebApplication.CreateBuilder(args);

// Add services to the container
builder.Services.AddEndpointsApiExplorer();
builder.Services.AddOpenApi();
builder.Services.AddInfrastructure(builder.Configuration);

// Configure ProblemDetails and Global Exception Handling (T10)
builder.Services.AddProblemDetails();
builder.Services.AddExceptionHandler<GlobalExceptionHandler>();

// Configure ASP.NET Core Native Rate Limiting (T10)
builder.Services.AddCustomRateLimiting(builder.Configuration);

// Configure Upload Limits (up to 55 MB to comfortably support 50 MB files)
builder.Services.Configure<FormOptions>(options =>
{
    options.MultipartBodyLengthLimit = 55 * 1024 * 1024;
});

// Configure Authentication with JWT Bearer
var jwtSecretKey = builder.Configuration["Jwt:SecretKey"] ?? "SuperSecretKeyForDevelopmentTesting1234567890!";
var jwtIssuer = builder.Configuration["Jwt:Issuer"] ?? "IgfAcademicRepo";
var jwtAudience = builder.Configuration["Jwt:Audience"] ?? "IgfAcademicRepoClient";

builder.Services.AddAuthentication(options =>
{
    options.DefaultAuthenticateScheme = JwtBearerDefaults.AuthenticationScheme;
    options.DefaultChallengeScheme = JwtBearerDefaults.AuthenticationScheme;
})
.AddJwtBearer(options =>
{
    options.TokenValidationParameters = new TokenValidationParameters
    {
        ValidateIssuerSigningKey = true,
        IssuerSigningKey = new SymmetricSecurityKey(Encoding.UTF8.GetBytes(jwtSecretKey)),
        ValidateIssuer = true,
        ValidIssuer = jwtIssuer,
        ValidateAudience = true,
        ValidAudience = jwtAudience,
        ValidateLifetime = true,
        ClockSkew = TimeSpan.Zero
    };
});

builder.Services.AddAuthorization(options =>
{
    options.AddPolicy("AdminOnly", policy => policy.RequireRole("Admin"));
    options.AddPolicy("DelegateOrAdmin", policy => policy.RequireRole("Admin", "Delegado"));
});

var app = builder.Build();

// Global Exception Handler (RFC 7807)
app.UseExceptionHandler();

// Configure the HTTP request pipeline
if (app.Environment.IsDevelopment())
{
    app.MapOpenApi();
}

app.UseHttpsRedirection();

app.UseAuthentication();
app.UseRateLimiter();
app.UseAuthorization();

// Root & Health endpoints
app.MapGet("/", () => Results.Ok(new { message = "IGF Academic Repository API" }))
   .WithName("GetRoot");

app.MapGet("/health", () => Results.Ok(new { status = "Healthy" }))
   .WithName("GetHealth");

// Map Materials Endpoints (T06 & T07 & T09)
app.MapMaterialEndpoints();

// Map Admin Materials Moderation Endpoints (T08)
app.MapAdminMaterialEndpoints();

// Map Authentication Endpoints (T11)
app.MapAuthEndpoints();

// Diagnostic test endpoints for exception handling and rate limiting verification
if (app.Environment.IsDevelopment() || app.Environment.EnvironmentName == "Testing")
{
    var testErrors = app.MapGroup("/api/v1/test/errors");
    testErrors.MapGet("/validation", () => { throw new ValidationException("Falha de validação simulada."); });
    testErrors.MapGet("/not-found", () => { throw new MaterialNotFoundException(Guid.NewGuid()); });
    testErrors.MapGet("/forbidden", () => { throw new ForbiddenOperationException("Operação negada simulada."); });
    testErrors.MapGet("/file-size", () => { throw new FileSizeLimitExceededException(50 * 1024 * 1024, 60 * 1024 * 1024); });
    testErrors.MapGet("/unhandled", () => { throw new InvalidOperationException("Erro inesperado simulado."); });
}

app.Run();

// Expose Program class for WebApplicationFactory in integration tests
public partial class Program { }
