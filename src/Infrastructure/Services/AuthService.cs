using Core.DTOs;
using Core.Interfaces;
using Infrastructure.Data;
using Infrastructure.Security;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace Infrastructure.Services;

public class AuthService : IAuthService
{
    private readonly AppDbContext _dbContext;
    private readonly IPasswordHasher _passwordHasher;
    private readonly IJwtTokenService _jwtTokenService;
    private readonly IRefreshTokenService _refreshTokenService;
    private readonly JwtOptions _jwtOptions;
    private readonly ILogger<AuthService> _logger;

    public AuthService(
        AppDbContext dbContext,
        IPasswordHasher passwordHasher,
        IJwtTokenService jwtTokenService,
        IRefreshTokenService refreshTokenService,
        IOptions<JwtOptions> jwtOptions,
        ILogger<AuthService> logger)
    {
        _dbContext = dbContext;
        _passwordHasher = passwordHasher;
        _jwtTokenService = jwtTokenService;
        _refreshTokenService = refreshTokenService;
        _jwtOptions = jwtOptions.Value;
        _logger = logger;
    }

    public async Task<AuthLoginResult?> LoginAsync(LoginRequest request, CancellationToken cancellationToken = default)
    {
        if (request == null || string.IsNullOrWhiteSpace(request.Email) || string.IsNullOrWhiteSpace(request.Password))
        {
            return null;
        }

        var normalizedEmail = request.Email.Trim().ToLowerInvariant();
        var user = await _dbContext.Users
            .FirstOrDefaultAsync(u => u.Email.ToLower() == normalizedEmail, cancellationToken);

        if (user == null || !user.IsActive)
        {
            _logger.LogWarning("Tentativa de login falhou: utilizador {Email} não encontrado ou inativo.", request.Email);
            return null;
        }

        if (!_passwordHasher.VerifyPassword(request.Password, user.PasswordHash))
        {
            _logger.LogWarning("Tentativa de login falhou: credenciais inválidas para {Email}.", request.Email);
            return null;
        }

        var accessToken = _jwtTokenService.GenerateAccessToken(user);
        var (rawRefreshToken, tokenEntity) = await _refreshTokenService.CreateAndSaveRefreshTokenAsync(user.Id, cancellationToken);

        _logger.LogInformation("Utilizador {UserId} ({Role}) autenticado com sucesso.", user.Id, user.Role);

        return new AuthLoginResult(
            AccessToken: accessToken,
            RawRefreshToken: rawRefreshToken,
            RefreshTokenExpiresAt: tokenEntity.ExpiresAt,
            ExpiresInMinutes: _jwtOptions.AccessTokenExpirationMinutes,
            User: new AuthUserDto(user.Id, user.Name, user.Email, user.Role.ToString())
        );
    }

    public async Task<AuthRefreshResult?> RefreshTokenAsync(string rawRefreshToken, CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(rawRefreshToken))
        {
            return null;
        }

        var rotation = await _refreshTokenService.RotateRefreshTokenAsync(rawRefreshToken, cancellationToken);
        if (rotation == null)
        {
            _logger.LogWarning("Tentativa de renovação de sessão falhou para o refresh token fornecido.");
            return null;
        }

        var expiresAt = DateTime.UtcNow.AddDays(_jwtOptions.RefreshTokenExpirationDays);

        return new AuthRefreshResult(
            NewAccessToken: rotation.Value.NewAccessToken,
            NewRefreshToken: rotation.Value.NewRefreshToken,
            RefreshTokenExpiresAt: expiresAt,
            ExpiresInMinutes: _jwtOptions.AccessTokenExpirationMinutes
        );
    }
}
