using Core.DTOs;

namespace Core.Interfaces;

public record AuthLoginResult(
    string AccessToken,
    string RawRefreshToken,
    DateTime RefreshTokenExpiresAt,
    int ExpiresInMinutes,
    AuthUserDto User
);

public record AuthRefreshResult(
    string NewAccessToken,
    string NewRefreshToken,
    DateTime RefreshTokenExpiresAt,
    int ExpiresInMinutes
);

public interface IAuthService
{
    Task<AuthLoginResult?> LoginAsync(LoginRequest request, CancellationToken cancellationToken = default);
    Task<AuthRefreshResult?> RefreshTokenAsync(string rawRefreshToken, CancellationToken cancellationToken = default);
}
