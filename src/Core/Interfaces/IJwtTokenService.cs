using System.Security.Claims;
using Core.Entities;

namespace Core.Interfaces;

public interface IJwtTokenService
{
    string GenerateAccessToken(User user);
    ClaimsPrincipal? ValidateAccessToken(string token, bool validateLifetime = true);
    string GenerateRefreshToken();
    string HashToken(string token);
}
