using Core.Entities;
using Core.Enums;
using FluentAssertions;
using Infrastructure.Data;
using Infrastructure.Security;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;

namespace UnitTests;

public class RefreshTokenServiceTests
{
    private readonly JwtOptions _options = new()
    {
        Issuer = "TestIssuer",
        Audience = "TestAudience",
        SecretKey = "SuperSecretKeyForRefreshTokenTesting1234567890!",
        AccessTokenExpirationMinutes = 15,
        RefreshTokenExpirationDays = 7
    };

    private AppDbContext CreateInMemoryDbContext()
    {
        var options = new DbContextOptionsBuilder<AppDbContext>()
            .UseInMemoryDatabase(databaseName: Guid.NewGuid().ToString())
            .Options;

        return new AppDbContext(options);
    }

    private (RefreshTokenService service, AppDbContext context, JwtTokenService jwtService) CreateSut()
    {
        var context = CreateInMemoryDbContext();
        var jwtOptions = Options.Create(_options);
        var jwtService = new JwtTokenService(jwtOptions);
        var service = new RefreshTokenService(context, jwtService, jwtOptions);
        return (service, context, jwtService);
    }

    private async Task<User> SeedUserAsync(AppDbContext context, bool isActive = true)
    {
        var user = new User
        {
            Id = Guid.NewGuid(),
            Name = "John Doe",
            Email = $"user_{Guid.NewGuid():N}@isutic.ed.ao",
            PasswordHash = "hash",
            Role = UserRole.Delegado,
            IsActive = isActive,
            CreatedAt = DateTime.UtcNow
        };

        context.Users.Add(user);
        await context.SaveChangesAsync();
        return user;
    }

    [Fact]
    public async Task CreateAndSaveRefreshTokenAsync_ShouldCreateAndPersistToken()
    {
        var (sut, context, jwtService) = CreateSut();
        var user = await SeedUserAsync(context);

        var (rawToken, entity) = await sut.CreateAndSaveRefreshTokenAsync(user.Id);

        rawToken.Should().NotBeNullOrWhiteSpace();
        entity.Should().NotBeNull();
        entity.UserId.Should().Be(user.Id);
        entity.IsRevoked.Should().BeFalse();
        entity.ExpiresAt.Should().BeAfter(DateTime.UtcNow.AddDays(6));

        var dbToken = await context.RefreshTokens.FindAsync(entity.Id);
        dbToken.Should().NotBeNull();
        dbToken!.TokenHash.Should().Be(jwtService.HashToken(rawToken));
    }

    [Fact]
    public async Task RotateRefreshTokenAsync_WithValidToken_ShouldRotateAndRevokeOldToken()
    {
        var (sut, context, jwtService) = CreateSut();
        var user = await SeedUserAsync(context);

        var (initialRawToken, initialEntity) = await sut.CreateAndSaveRefreshTokenAsync(user.Id);

        var rotationResult = await sut.RotateRefreshTokenAsync(initialRawToken);

        rotationResult.Should().NotBeNull();
        var (newAccessToken, newRefreshToken) = rotationResult!.Value;

        newAccessToken.Should().NotBeNullOrWhiteSpace();
        newRefreshToken.Should().NotBeNullOrWhiteSpace();
        newRefreshToken.Should().NotBe(initialRawToken);

        // Verify old token is revoked
        var oldTokenInDb = await context.RefreshTokens.FindAsync(initialEntity.Id);
        oldTokenInDb!.IsRevoked.Should().BeTrue();
        oldTokenInDb.ReplacedByToken.Should().Be(jwtService.HashToken(newRefreshToken));

        // Verify new token exists in DB
        var newHash = jwtService.HashToken(newRefreshToken);
        var newTokenInDb = await context.RefreshTokens.FirstOrDefaultAsync(r => r.TokenHash == newHash);
        newTokenInDb.Should().NotBeNull();
        newTokenInDb!.IsRevoked.Should().BeFalse();
        newTokenInDb.UserId.Should().Be(user.Id);
    }

    [Fact]
    public async Task RotateRefreshTokenAsync_WithAlreadyRevokedToken_ShouldReturnNull()
    {
        var (sut, context, _) = CreateSut();
        var user = await SeedUserAsync(context);

        var (rawToken, _) = await sut.CreateAndSaveRefreshTokenAsync(user.Id);

        // First rotation succeeds
        var firstRotation = await sut.RotateRefreshTokenAsync(rawToken);
        firstRotation.Should().NotBeNull();

        // Attempting to rotate the same token again (replay attack)
        var secondRotation = await sut.RotateRefreshTokenAsync(rawToken);
        secondRotation.Should().BeNull();
    }

    [Fact]
    public async Task RotateRefreshTokenAsync_WithExpiredToken_ShouldReturnNull()
    {
        var (sut, context, jwtService) = CreateSut();
        var user = await SeedUserAsync(context);

        var rawToken = "expired_raw_token";
        var expiredToken = new RefreshToken
        {
            Id = Guid.NewGuid(),
            UserId = user.Id,
            TokenHash = jwtService.HashToken(rawToken),
            ExpiresAt = DateTime.UtcNow.AddMinutes(-10),
            IsRevoked = false,
            CreatedAt = DateTime.UtcNow.AddDays(-8)
        };

        context.RefreshTokens.Add(expiredToken);
        await context.SaveChangesAsync();

        var result = await sut.RotateRefreshTokenAsync(rawToken);

        result.Should().BeNull();
    }

    [Fact]
    public async Task RotateRefreshTokenAsync_WithInactiveUser_ShouldReturnNull()
    {
        var (sut, context, _) = CreateSut();
        var inactiveUser = await SeedUserAsync(context, isActive: false);

        var (rawToken, _) = await sut.CreateAndSaveRefreshTokenAsync(inactiveUser.Id);

        var result = await sut.RotateRefreshTokenAsync(rawToken);

        result.Should().BeNull();
    }

    [Fact]
    public async Task RotateRefreshTokenAsync_WithUnknownToken_ShouldReturnNull()
    {
        var (sut, _, _) = CreateSut();

        var result = await sut.RotateRefreshTokenAsync("non_existent_token_12345");

        result.Should().BeNull();
    }

    [Fact]
    public async Task RevokeRefreshTokenAsync_WithActiveToken_ShouldRevokeAndReturnTrue()
    {
        var (sut, context, _) = CreateSut();
        var user = await SeedUserAsync(context);

        var (rawToken, entity) = await sut.CreateAndSaveRefreshTokenAsync(user.Id);

        var revoked = await sut.RevokeRefreshTokenAsync(rawToken);

        revoked.Should().BeTrue();
        var tokenInDb = await context.RefreshTokens.FindAsync(entity.Id);
        tokenInDb!.IsRevoked.Should().BeTrue();
    }

    [Fact]
    public async Task RevokeRefreshTokenAsync_WithAlreadyRevokedOrUnknownToken_ShouldReturnFalse()
    {
        var (sut, context, _) = CreateSut();
        var user = await SeedUserAsync(context);

        var (rawToken, _) = await sut.CreateAndSaveRefreshTokenAsync(user.Id);
        await sut.RevokeRefreshTokenAsync(rawToken);

        // Revoking already revoked token
        var secondRevoke = await sut.RevokeRefreshTokenAsync(rawToken);
        secondRevoke.Should().BeFalse();

        // Revoking non-existent token
        var nonExistentRevoke = await sut.RevokeRefreshTokenAsync("unknown");
        nonExistentRevoke.Should().BeFalse();
    }

    [Fact]
    public async Task RevokeAllUserTokensAsync_ShouldRevokeAllActiveTokensForUser()
    {
        var (sut, context, _) = CreateSut();
        var user = await SeedUserAsync(context);

        await sut.CreateAndSaveRefreshTokenAsync(user.Id);
        await sut.CreateAndSaveRefreshTokenAsync(user.Id);
        await sut.CreateAndSaveRefreshTokenAsync(user.Id);

        var result = await sut.RevokeAllUserTokensAsync(user.Id);

        result.Should().BeTrue();
        var activeTokens = await context.RefreshTokens.Where(r => r.UserId == user.Id && !r.IsRevoked).ToListAsync();
        activeTokens.Should().BeEmpty();
    }
}
