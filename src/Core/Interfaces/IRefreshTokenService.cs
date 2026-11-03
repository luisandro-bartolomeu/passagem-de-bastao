using Core.Entities;

namespace Core.Interfaces;

public interface IRefreshTokenService
{
    Task<(string RawToken, RefreshToken Entity)> CreateAndSaveRefreshTokenAsync(Guid userId, CancellationToken cancellationToken = default);
    Task<(string NewAccessToken, string NewRefreshToken)?> RotateRefreshTokenAsync(string rawRefreshToken, CancellationToken cancellationToken = default);
    Task<bool> RevokeRefreshTokenAsync(string rawRefreshToken, CancellationToken cancellationToken = default);
    Task<bool> RevokeAllUserTokensAsync(Guid userId, CancellationToken cancellationToken = default);
}
