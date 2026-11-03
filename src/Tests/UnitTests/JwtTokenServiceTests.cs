using System.IdentityModel.Tokens.Jwt;
using System.Security.Claims;
using Core.Entities;
using Core.Enums;
using FluentAssertions;
using Infrastructure.Security;
using Microsoft.Extensions.Options;

namespace UnitTests;

public class JwtTokenServiceTests
{
    private readonly JwtOptions _defaultOptions = new()
    {
        Issuer = "TestIssuer",
        Audience = "TestAudience",
        SecretKey = "SuperSecretKeyForTestingJwtTokenGeneration123!",
        AccessTokenExpirationMinutes = 15,
        RefreshTokenExpirationDays = 7
    };

    private JwtTokenService CreateService(JwtOptions? options = null)
    {
        var opts = Options.Create(options ?? _defaultOptions);
        return new JwtTokenService(opts);
    }

    [Fact]
    public void GenerateAccessToken_ShouldReturnValidJwtToken()
    {
        var service = CreateService();
        var user = new User
        {
            Id = Guid.NewGuid(),
            Name = "John Doe",
            Email = "john.doe@isutic.ed.ao",
            Role = UserRole.Delegado
        };

        var tokenString = service.GenerateAccessToken(user);

        tokenString.Should().NotBeNullOrWhiteSpace();
        var handler = new JwtSecurityTokenHandler();
        handler.CanReadToken(tokenString).Should().BeTrue();

        var jwt = handler.ReadJwtToken(tokenString);
        jwt.Issuer.Should().Be(_defaultOptions.Issuer);
        jwt.Audiences.Should().Contain(_defaultOptions.Audience);
    }

    [Fact]
    public void GenerateAccessToken_ShouldIncludeUserClaimsAndRole()
    {
        var service = CreateService();
        var userId = Guid.NewGuid();
        var user = new User
        {
            Id = userId,
            Name = "Alice Admin",
            Email = "alice@isutic.ed.ao",
            Role = UserRole.Admin
        };

        var tokenString = service.GenerateAccessToken(user);
        var principal = service.ValidateAccessToken(tokenString);

        principal.Should().NotBeNull();
        principal!.FindFirst(ClaimTypes.NameIdentifier)?.Value.Should().Be(userId.ToString());
        principal.FindFirst(ClaimTypes.Email)?.Value.Should().Be("alice@isutic.ed.ao");
        principal.FindFirst(ClaimTypes.Name)?.Value.Should().Be("Alice Admin");
        principal.FindFirst(ClaimTypes.Role)?.Value.Should().Be(UserRole.Admin.ToString());
        principal.IsInRole("Admin").Should().BeTrue();
    }

    [Fact]
    public void ValidateAccessToken_WithValidToken_ShouldReturnClaimsPrincipal()
    {
        var service = CreateService();
        var user = new User
        {
            Id = Guid.NewGuid(),
            Name = "Estudante Bob",
            Email = "bob@isutic.ed.ao",
            Role = UserRole.Estudante
        };

        var tokenString = service.GenerateAccessToken(user);
        var principal = service.ValidateAccessToken(tokenString);

        principal.Should().NotBeNull();
        principal!.Identity!.IsAuthenticated.Should().BeTrue();
    }

    [Fact]
    public void ValidateAccessToken_WithExpiredToken_ShouldReturnNull()
    {
        var expiredOptions = new JwtOptions
        {
            Issuer = _defaultOptions.Issuer,
            Audience = _defaultOptions.Audience,
            SecretKey = _defaultOptions.SecretKey,
            AccessTokenExpirationMinutes = -5 // Expired 5 mins ago
        };
        var service = CreateService(expiredOptions);
        var user = new User
        {
            Id = Guid.NewGuid(),
            Name = "Expired User",
            Email = "expired@isutic.ed.ao",
            Role = UserRole.Estudante
        };

        var tokenString = service.GenerateAccessToken(user);
        var principal = service.ValidateAccessToken(tokenString, validateLifetime: true);

        principal.Should().BeNull();
    }

    [Fact]
    public void ValidateAccessToken_WithTamperedToken_ShouldReturnNull()
    {
        var service = CreateService();
        var user = new User
        {
            Id = Guid.NewGuid(),
            Name = "User",
            Email = "user@isutic.ed.ao",
            Role = UserRole.Estudante
        };

        var tokenString = service.GenerateAccessToken(user);
        var tamperedToken = tokenString + "corrupted";

        var principal = service.ValidateAccessToken(tamperedToken);

        principal.Should().BeNull();
    }

    [Fact]
    public void ValidateAccessToken_WithDifferentSecretKey_ShouldReturnNull()
    {
        var service1 = CreateService();
        var otherKeyOptions = new JwtOptions
        {
            Issuer = _defaultOptions.Issuer,
            Audience = _defaultOptions.Audience,
            SecretKey = "AnotherCompletelyDifferentSecretKeyForSigning12345!"
        };
        var service2 = CreateService(otherKeyOptions);

        var user = new User
        {
            Id = Guid.NewGuid(),
            Name = "User",
            Email = "user@isutic.ed.ao",
            Role = UserRole.Delegado
        };

        var tokenString = service1.GenerateAccessToken(user);
        var principal = service2.ValidateAccessToken(tokenString);

        principal.Should().BeNull();
    }

    [Fact]
    public void GenerateRefreshToken_ShouldProduceUniqueSecureTokens()
    {
        var service = CreateService();

        var token1 = service.GenerateRefreshToken();
        var token2 = service.GenerateRefreshToken();

        token1.Should().NotBeNullOrWhiteSpace();
        token2.Should().NotBeNullOrWhiteSpace();
        token1.Should().NotBe(token2);
        token1.Length.Should().BeGreaterThan(40);
    }

    [Fact]
    public void HashToken_ShouldBeDeterministicAndHexEncoded()
    {
        var service = CreateService();
        const string raw = "SampleRefreshTokenToHash123!";

        var hash1 = service.HashToken(raw);
        var hash2 = service.HashToken(raw);

        hash1.Should().Be(hash2);
        hash1.Length.Should().Be(64); // SHA-256 produces 32 bytes = 64 hex chars
    }
}
