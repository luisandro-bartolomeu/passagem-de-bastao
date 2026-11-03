using Core.Entities;
using Core.Interfaces;
using Infrastructure.Data;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;

namespace Infrastructure.Security;

public class RefreshTokenService : IRefreshTokenService
{
    private readonly AppDbContext _dbContext;
    private readonly IJwtTokenService _jwtTokenService;
    private readonly JwtOptions _options;

    public RefreshTokenService(
        AppDbContext dbContext,
        IJwtTokenService jwtTokenService,
        IOptions<JwtOptions> options)
    {
        _dbContext = dbContext;
        _jwtTokenService = jwtTokenService;
        _options = options.Value;
    }

    public async Task<(string RawToken, RefreshToken Entity)> CreateAndSaveRefreshTokenAsync(
        Guid userId,
        CancellationToken cancellationToken = default)
    {
        var rawToken = _jwtTokenService.GenerateRefreshToken();
        var tokenHash = _jwtTokenService.HashToken(rawToken);

        var entity = new RefreshToken
        {
            Id = Guid.NewGuid(),
            UserId = userId,
            TokenHash = tokenHash,
            ExpiresAt = DateTime.UtcNow.AddDays(_options.RefreshTokenExpirationDays),
            IsRevoked = false,
            CreatedAt = DateTime.UtcNow
        };

        _dbContext.RefreshTokens.Add(entity);
        await _dbContext.SaveChangesAsync(cancellationToken);

        return (rawToken, entity);
    }

    public async Task<(string NewAccessToken, string NewRefreshToken)?> RotateRefreshTokenAsync(
        string rawRefreshToken,
        CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(rawRefreshToken))
        {
            return null;
        }

        var tokenHash = _jwtTokenService.HashToken(rawRefreshToken);
        var existingToken = await _dbContext.RefreshTokens
            .Include(r => r.User)
            .FirstOrDefaultAsync(r => r.TokenHash == tokenHash, cancellationToken);

        if (existingToken == null || existingToken.IsRevoked || existingToken.IsExpired || !existingToken.User.IsActive)
        {
            return null;
        }

        var newRawToken = _jwtTokenService.GenerateRefreshToken();
        var newTokenHash = _jwtTokenService.HashToken(newRawToken);

        var newRefreshToken = new RefreshToken
        {
            Id = Guid.NewGuid(),
            UserId = existingToken.UserId,
            TokenHash = newTokenHash,
            ExpiresAt = DateTime.UtcNow.AddDays(_options.RefreshTokenExpirationDays),
            IsRevoked = false,
            CreatedAt = DateTime.UtcNow
        };

        existingToken.IsRevoked = true;
        existingToken.ReplacedByToken = newTokenHash;

        _dbContext.RefreshTokens.Add(newRefreshToken);
        await _dbContext.SaveChangesAsync(cancellationToken);

        var newAccessToken = _jwtTokenService.GenerateAccessToken(existingToken.User);

        return (newAccessToken, newRawToken);
    }

    public async Task<bool> RevokeRefreshTokenAsync(
        string rawRefreshToken,
        CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(rawRefreshToken))
        {
            return false;
        }

        var tokenHash = _jwtTokenService.HashToken(rawRefreshToken);
        var existingToken = await _dbContext.RefreshTokens
            .FirstOrDefaultAsync(r => r.TokenHash == tokenHash, cancellationToken);

        if (existingToken == null || existingToken.IsRevoked)
        {
            return false;
        }

        existingToken.IsRevoked = true;
        await _dbContext.SaveChangesAsync(cancellationToken);

        return true;
    }

    public async Task<bool> RevokeAllUserTokensAsync(
        Guid userId,
        CancellationToken cancellationToken = default)
    {
        var activeTokens = await _dbContext.RefreshTokens
            .Where(r => r.UserId == userId && !r.IsRevoked)
            .ToListAsync(cancellationToken);

        if (activeTokens.Count == 0)
        {
            return false;
        }

        foreach (var token in activeTokens)
        {
            token.IsRevoked = true;
        }

        await _dbContext.SaveChangesAsync(cancellationToken);
        return true;
    }
}
