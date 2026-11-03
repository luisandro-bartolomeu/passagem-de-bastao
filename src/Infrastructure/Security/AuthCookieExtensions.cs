using Microsoft.AspNetCore.Http;

namespace Infrastructure.Security;

public static class AuthCookieExtensions
{
    public const string RefreshTokenCookieName = "igf_refresh_token";
    public const string AuthPath = "/api/v1/auth";

    public static void AppendRefreshTokenCookie(
        this HttpResponse response,
        string refreshToken,
        DateTime expiresAt,
        string path = AuthPath)
    {
        ArgumentNullException.ThrowIfNull(response);
        ArgumentException.ThrowIfNullOrWhiteSpace(refreshToken);

        var cookieOptions = new CookieOptions
        {
            HttpOnly = true,
            Secure = true,
            SameSite = SameSiteMode.Strict,
            Expires = expiresAt,
            Path = path,
            IsEssential = true
        };

        response.Cookies.Append(RefreshTokenCookieName, refreshToken, cookieOptions);
    }

    public static void DeleteRefreshTokenCookie(
        this HttpResponse response,
        string path = AuthPath)
    {
        ArgumentNullException.ThrowIfNull(response);

        var cookieOptions = new CookieOptions
        {
            HttpOnly = true,
            Secure = true,
            SameSite = SameSiteMode.Strict,
            Path = path,
            Expires = DateTimeOffset.UtcNow.AddDays(-1),
            IsEssential = true
        };

        response.Cookies.Delete(RefreshTokenCookieName, cookieOptions);
    }
}
