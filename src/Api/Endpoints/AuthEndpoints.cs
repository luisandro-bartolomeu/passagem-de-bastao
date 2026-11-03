using Api.Middlewares;
using Core.DTOs;
using Core.Interfaces;
using Infrastructure.Security;
using Microsoft.AspNetCore.Mvc;

namespace Api.Endpoints;

public static class AuthEndpoints
{
    public static IEndpointRouteBuilder MapAuthEndpoints(this IEndpointRouteBuilder app)
    {
        var group = app.MapGroup("/api/v1/auth")
            .WithTags("Auth")
            .RequireRateLimiting(RateLimitingExtensions.StrictPolicy);

        group.MapPost("/login", Login)
            .WithName("AuthLogin")
            .AllowAnonymous();

        group.MapPost("/refresh", Refresh)
            .WithName("AuthRefresh")
            .AllowAnonymous();

        return app;
    }

    public static async Task<IResult> Login(
        [FromBody] LoginRequest? request,
        IAuthService authService,
        HttpResponse response,
        CancellationToken cancellationToken)
    {
        if (request == null || string.IsNullOrWhiteSpace(request.Email) || string.IsNullOrWhiteSpace(request.Password))
        {
            return Results.Problem(
                statusCode: StatusCodes.Status400BadRequest,
                title: "Dados Incompletos",
                detail: "Email e palavra-passe são obrigatórios.");
        }

        var result = await authService.LoginAsync(request, cancellationToken);
        if (result == null)
        {
            return Results.Problem(
                statusCode: StatusCodes.Status401Unauthorized,
                title: "Credenciais Inválidas",
                detail: "Email ou palavra-passe incorretos, ou utilizador inativo.");
        }

        // Set HttpOnly, Secure, SameSite=Strict cookie for Refresh Token
        response.AppendRefreshTokenCookie(result.RawRefreshToken, result.RefreshTokenExpiresAt);

        var responseDto = new AuthResponseDto(
            AccessToken: result.AccessToken,
            TokenType: "Bearer",
            ExpiresInMinutes: result.ExpiresInMinutes,
            User: result.User
        );

        return Results.Ok(responseDto);
    }

    public static async Task<IResult> Refresh(
        HttpRequest request,
        HttpResponse response,
        [FromBody] RefreshTokenRequest? body,
        IAuthService authService,
        CancellationToken cancellationToken)
    {
        var rawToken = request.Cookies[AuthCookieExtensions.RefreshTokenCookieName];
        if (string.IsNullOrWhiteSpace(rawToken) && body != null)
        {
            rawToken = body.RefreshToken;
        }

        if (string.IsNullOrWhiteSpace(rawToken))
        {
            return Results.Problem(
                statusCode: StatusCodes.Status401Unauthorized,
                title: "Token Não Encontrado",
                detail: "Nenhum refresh token foi fornecido na requisição.");
        }

        var result = await authService.RefreshTokenAsync(rawToken, cancellationToken);
        if (result == null)
        {
            response.DeleteRefreshTokenCookie();
            return Results.Problem(
                statusCode: StatusCodes.Status401Unauthorized,
                title: "Sessão Inválida",
                detail: "O refresh token expirou, foi revogado ou é inválido.");
        }

        response.AppendRefreshTokenCookie(result.NewRefreshToken, result.RefreshTokenExpiresAt);

        return Results.Ok(new
        {
            accessToken = result.NewAccessToken,
            tokenType = "Bearer",
            expiresInMinutes = result.ExpiresInMinutes
        });
    }
}
