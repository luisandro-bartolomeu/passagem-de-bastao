using Core.Exceptions;
using Microsoft.AspNetCore.Diagnostics;
using Microsoft.AspNetCore.Mvc;

namespace Api.Middlewares;

public class GlobalExceptionHandler : IExceptionHandler
{
    private readonly ILogger<GlobalExceptionHandler> _logger;

    public GlobalExceptionHandler(ILogger<GlobalExceptionHandler> logger)
    {
        _logger = logger;
    }

    public async ValueTask<bool> TryHandleAsync(
        HttpContext httpContext,
        Exception exception,
        CancellationToken cancellationToken)
    {
        _logger.LogError(exception, "Exceção não tratada capturada pelo GlobalExceptionHandler: {Message}", exception.Message);

        var (statusCode, title, detail) = MapException(exception);

        httpContext.Response.StatusCode = statusCode;

        var problemDetails = new ProblemDetails
        {
            Status = statusCode,
            Title = title,
            Detail = detail,
            Instance = httpContext.Request.Path
        };

        var problemDetailsService = httpContext.RequestServices.GetService<IProblemDetailsService>();
        if (problemDetailsService != null && await problemDetailsService.TryWriteAsync(new ProblemDetailsContext
        {
            HttpContext = httpContext,
            ProblemDetails = problemDetails
        }))
        {
            return true;
        }

        await httpContext.Response.WriteAsJsonAsync(problemDetails, options: null, contentType: "application/problem+json", cancellationToken: cancellationToken);
        return true;
    }

    private static (int StatusCode, string Title, string Detail) MapException(Exception exception)
    {
        return exception switch
        {
            ValidationException ex => (
                StatusCodes.Status400BadRequest,
                "Erro de Validação",
                ex.Message
            ),
            InvalidFileFormatException ex => (
                StatusCodes.Status400BadRequest,
                "Formato de Ficheiro Inválido",
                ex.Message
            ),
            SubjectNotFoundException ex => (
                StatusCodes.Status400BadRequest,
                "Disciplina Não Encontrada",
                ex.Message
            ),
            FileSizeLimitExceededException ex => (
                StatusCodes.Status413PayloadTooLarge,
                "Payload Too Large",
                ex.Message
            ),
            MaterialNotFoundException ex => (
                StatusCodes.Status404NotFound,
                "Material Não Encontrado",
                ex.Message
            ),
            ForbiddenOperationException ex => (
                StatusCodes.Status403Forbidden,
                "Operação Proibida",
                ex.Message
            ),
            UnauthorizedAccessException ex => (
                StatusCodes.Status401Unauthorized,
                "Não Autorizado",
                ex.Message
            ),
            KeyNotFoundException ex => (
                StatusCodes.Status404NotFound,
                "Recurso Não Encontrado",
                ex.Message
            ),
            ArgumentException ex => (
                StatusCodes.Status400BadRequest,
                "Parâmetro Inválido",
                ex.Message
            ),
            _ => (
                StatusCodes.Status500InternalServerError,
                "Erro Interno do Servidor",
                "Ocorreu um erro interno inesperado no servidor."
            )
        };
    }
}
