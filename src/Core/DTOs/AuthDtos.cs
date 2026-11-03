namespace Core.DTOs;

public record LoginRequest(string Email, string Password);

public record AuthResponseDto(
    string AccessToken,
    string TokenType,
    int ExpiresInMinutes,
    AuthUserDto User
);

public record AuthUserDto(
    Guid Id,
    string Name,
    string Email,
    string Role
);

public record RefreshTokenRequest(string? RefreshToken = null);
