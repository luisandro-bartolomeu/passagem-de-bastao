using FluentAssertions;
using Infrastructure.Security;
using Microsoft.AspNetCore.Http;

namespace UnitTests;

public class AuthCookieExtensionsTests
{
    [Fact]
    public void AppendRefreshTokenCookie_ShouldSetHttpOnlySecureStrictCookie()
    {
        var httpContext = new DefaultHttpContext();
        var expiresAt = DateTime.UtcNow.AddDays(7);
        const string rawToken = "sample_secure_refresh_token_value";

        httpContext.Response.AppendRefreshTokenCookie(rawToken, expiresAt);

        var setCookieHeader = httpContext.Response.Headers.SetCookie.ToString();
        setCookieHeader.Should().Contain(AuthCookieExtensions.RefreshTokenCookieName);
        setCookieHeader.Should().Contain(rawToken);
        setCookieHeader.Should().Contain("httponly");
        setCookieHeader.Should().Contain("secure");
        setCookieHeader.Should().Contain("samesite=strict");
        setCookieHeader.Should().Contain($"path={AuthCookieExtensions.AuthPath}");
    }

    [Fact]
    public void DeleteRefreshTokenCookie_ShouldSetExpiredCookieWithMatchingPath()
    {
        var httpContext = new DefaultHttpContext();

        httpContext.Response.DeleteRefreshTokenCookie();

        var setCookieHeader = httpContext.Response.Headers.SetCookie.ToString();
        setCookieHeader.Should().Contain(AuthCookieExtensions.RefreshTokenCookieName);
        setCookieHeader.Should().Contain("httponly");
        setCookieHeader.Should().Contain("secure");
        setCookieHeader.Should().Contain("samesite=strict");
        setCookieHeader.Should().Contain($"path={AuthCookieExtensions.AuthPath}");
        setCookieHeader.Should().Contain("expires=");
    }
}
