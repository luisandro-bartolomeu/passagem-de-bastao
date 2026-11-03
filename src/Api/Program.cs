using System.Text;
using Infrastructure;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.IdentityModel.Tokens;

var builder = WebApplication.CreateBuilder(args);

// Add services to the container
builder.Services.AddEndpointsApiExplorer();
builder.Services.AddOpenApi();
builder.Services.AddInfrastructure(builder.Configuration);

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

// Configure the HTTP request pipeline
if (app.Environment.IsDevelopment())
{
    app.MapOpenApi();
}

app.UseHttpsRedirection();

app.UseAuthentication();
app.UseAuthorization();

// Root & Health endpoints
app.MapGet("/", () => Results.Ok(new { message = "IGF Academic Repository API" }))
   .WithName("GetRoot");

app.MapGet("/health", () => Results.Ok(new { status = "Healthy" }))
   .WithName("GetHealth");

app.Run();

// Expose Program class for WebApplicationFactory in integration tests
public partial class Program { }
