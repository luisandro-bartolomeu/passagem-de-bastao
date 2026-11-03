using Core.Interfaces;
using Infrastructure.Data;
using Infrastructure.Email;
using Infrastructure.Security;
using Infrastructure.Services;
using Infrastructure.Storage;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;

namespace Infrastructure;

public static class DependencyInjection
{
    public static IServiceCollection AddInfrastructure(this IServiceCollection services, IConfiguration configuration)
    {
        var connectionString = configuration.GetConnectionString("DefaultConnection")
            ?? "Host=localhost;Port=5432;Database=igf_academic_repo;Username=postgres;Password=postgres";

        services.AddDbContext<AppDbContext>(options =>
            options.UseNpgsql(connectionString));

        // JWT Configuration
        services.Configure<JwtOptions>(options =>
        {
            var section = configuration.GetSection(JwtOptions.SectionName);
            section.Bind(options);
        });

        // Storage Configuration
        services.Configure<LocalStorageOptions>(options =>
        {
            var section = configuration.GetSection(LocalStorageOptions.SectionName);
            section.Bind(options);
        });

        services.Configure<S3StorageOptions>(options =>
        {
            var section = configuration.GetSection(S3StorageOptions.SectionName);
            section.Bind(options);
        });

        // Email Configuration
        services.Configure<EmailOptions>(options =>
        {
            var section = configuration.GetSection(EmailOptions.SectionName);
            section.Bind(options);
        });

        // Security & Authentication Services
        services.AddSingleton<IPasswordHasher, BCryptPasswordHasher>();
        services.AddSingleton<IJwtTokenService, JwtTokenService>();
        services.AddScoped<IRefreshTokenService, RefreshTokenService>();

        // Storage Services
        services.AddSingleton<IFileValidator, FileValidator>();
        services.AddScoped<IFileStorageService, LocalFileStorageService>();

        // Email & Background Queue Services
        services.AddSingleton<IEmailNotificationQueue, EmailNotificationQueue>();
        services.AddSingleton<IEmailSender, MailKitEmailSender>();
        services.AddHostedService<EmailNotificationBackgroundService>();

        // Material Domain Services
        services.AddScoped<IMaterialService, MaterialService>();

        return services;
    }
}
