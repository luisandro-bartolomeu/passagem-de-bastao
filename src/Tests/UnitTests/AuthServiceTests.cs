using Core.DTOs;
using Core.Entities;
using Core.Enums;
using Core.Interfaces;
using FluentAssertions;
using Infrastructure.Data;
using Infrastructure.Security;
using Infrastructure.Services;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;

namespace UnitTests;

public class AuthServiceTests
{
    private AppDbContext CreateDbContext()
    {
        var options = new DbContextOptionsBuilder<AppDbContext>()
            .UseInMemoryDatabase("AuthServiceTest_" + Guid.NewGuid().ToString("N"))
            .Options;
        return new AppDbContext(options);
    }

    private static IOptions<JwtOptions> CreateJwtOptions() =>
        Options.Create(new JwtOptions
        {
            Issuer = "TestIssuer",
            Audience = "TestAudience",
            SecretKey = "SuperSecretKeyForDevelopmentTesting1234567890!",
            AccessTokenExpirationMinutes = 15,
            RefreshTokenExpirationDays = 7
        });

    [Fact]
    public async Task LoginAsync_WithValidCredentials_ShouldReturnAuthLoginResult()
    {
        using var context = CreateDbContext();
        var passwordHasher = new BCryptPasswordHasher();
        var jwtOptions = CreateJwtOptions();
        var jwtService = new JwtTokenService(jwtOptions);
        var refreshService = new RefreshTokenService(context, jwtService, jwtOptions);

        var rawPassword = "SecurePassword123!";
        var user = new User
        {
            Id = Guid.NewGuid(),
            Name = "Delegado Silva",
            Email = "delegado@isutic.ed.ao",
            PasswordHash = passwordHasher.HashPassword(rawPassword),
            Role = UserRole.Delegado,
            IsActive = true
        };

        context.Users.Add(user);
        await context.SaveChangesAsync();

        var sut = new AuthService(context, passwordHasher, jwtService, refreshService, jwtOptions, NullLogger<AuthService>.Instance);

        var result = await sut.LoginAsync(new LoginRequest(user.Email, rawPassword));

        result.Should().NotBeNull();
        result!.AccessToken.Should().NotBeNullOrWhiteSpace();
        result.RawRefreshToken.Should().NotBeNullOrWhiteSpace();
        result.ExpiresInMinutes.Should().Be(15);
        result.User.Email.Should().Be(user.Email);
        result.User.Role.Should().Be("Delegado");
    }

    [Fact]
    public async Task LoginAsync_WithWrongPassword_ShouldReturnNull()
    {
        using var context = CreateDbContext();
        var passwordHasher = new BCryptPasswordHasher();
        var jwtOptions = CreateJwtOptions();
        var jwtService = new JwtTokenService(jwtOptions);
        var refreshService = new RefreshTokenService(context, jwtService, jwtOptions);

        var user = new User
        {
            Id = Guid.NewGuid(),
            Name = "Delegado Silva",
            Email = "delegado@isutic.ed.ao",
            PasswordHash = passwordHasher.HashPassword("CorrectPassword"),
            Role = UserRole.Delegado,
            IsActive = true
        };

        context.Users.Add(user);
        await context.SaveChangesAsync();

        var sut = new AuthService(context, passwordHasher, jwtService, refreshService, jwtOptions, NullLogger<AuthService>.Instance);

        var result = await sut.LoginAsync(new LoginRequest(user.Email, "WrongPassword"));

        result.Should().BeNull();
    }

    [Fact]
    public async Task LoginAsync_WithInactiveUser_ShouldReturnNull()
    {
        using var context = CreateDbContext();
        var passwordHasher = new BCryptPasswordHasher();
        var jwtOptions = CreateJwtOptions();
        var jwtService = new JwtTokenService(jwtOptions);
        var refreshService = new RefreshTokenService(context, jwtService, jwtOptions);

        var user = new User
        {
            Id = Guid.NewGuid(),
            Name = "Delegado Inativo",
            Email = "inativo@isutic.ed.ao",
            PasswordHash = passwordHasher.HashPassword("Password123!"),
            Role = UserRole.Delegado,
            IsActive = false // Inactive
        };

        context.Users.Add(user);
        await context.SaveChangesAsync();

        var sut = new AuthService(context, passwordHasher, jwtService, refreshService, jwtOptions, NullLogger<AuthService>.Instance);

        var result = await sut.LoginAsync(new LoginRequest(user.Email, "Password123!"));

        result.Should().BeNull();
    }

    [Fact]
    public async Task RefreshTokenAsync_WithValidToken_ShouldReturnNewTokens()
    {
        using var context = CreateDbContext();
        var passwordHasher = new BCryptPasswordHasher();
        var jwtOptions = CreateJwtOptions();
        var jwtService = new JwtTokenService(jwtOptions);
        var refreshService = new RefreshTokenService(context, jwtService, jwtOptions);

        var user = new User
        {
            Id = Guid.NewGuid(),
            Name = "Estudante Santos",
            Email = "estudante@isutic.ed.ao",
            PasswordHash = passwordHasher.HashPassword("Password123!"),
            Role = UserRole.Estudante,
            IsActive = true
        };

        context.Users.Add(user);
        await context.SaveChangesAsync();

        var (rawRefreshToken, _) = await refreshService.CreateAndSaveRefreshTokenAsync(user.Id);

        var sut = new AuthService(context, passwordHasher, jwtService, refreshService, jwtOptions, NullLogger<AuthService>.Instance);

        var result = await sut.RefreshTokenAsync(rawRefreshToken);

        result.Should().NotBeNull();
        result!.NewAccessToken.Should().NotBeNullOrWhiteSpace();
        result.NewRefreshToken.Should().NotBeNullOrWhiteSpace();
        result.NewRefreshToken.Should().NotBe(rawRefreshToken);
    }

    [Fact]
    public async Task RefreshTokenAsync_WithInvalidToken_ShouldReturnNull()
    {
        using var context = CreateDbContext();
        var passwordHasher = new BCryptPasswordHasher();
        var jwtOptions = CreateJwtOptions();
        var jwtService = new JwtTokenService(jwtOptions);
        var refreshService = new RefreshTokenService(context, jwtService, jwtOptions);

        var sut = new AuthService(context, passwordHasher, jwtService, refreshService, jwtOptions, NullLogger<AuthService>.Instance);

        var result = await sut.RefreshTokenAsync("invalid-token-value");

        result.Should().BeNull();
    }
}
