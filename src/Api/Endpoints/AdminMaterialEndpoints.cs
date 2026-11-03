using Core.DTOs;
using Core.Enums;
using Core.Exceptions;
using Core.Interfaces;
using Microsoft.AspNetCore.Mvc;

namespace Api.Endpoints;

public static class AdminMaterialEndpoints
{
    public static IEndpointRouteBuilder MapAdminMaterialEndpoints(this IEndpointRouteBuilder app)
    {
        var group = app.MapGroup("/api/v1/admin/materials")
            .WithTags("AdminMaterials")
            .RequireAuthorization("AdminOnly");

        group.MapPatch("/{id:guid}/status", UpdateMaterialStatus)
            .WithName("UpdateMaterialStatus");

        return app;
    }

    public static async Task<IResult> UpdateMaterialStatus(
        Guid id,
        [FromBody] UpdateMaterialStatusRequest? request,
        IMaterialService materialService,
        CancellationToken cancellationToken)
    {
        if (request == null)
        {
            return Results.Problem(
                statusCode: StatusCodes.Status400BadRequest,
                title: "Corpo da Requisição Inválido",
                detail: "Os dados de moderação são obrigatórios.");
        }

        if (!TryParseModerationStatus(request.Status, out var status))
        {
            return Results.Problem(
                statusCode: StatusCodes.Status400BadRequest,
                title: "Estado Inválido",
                detail: "O estado pretendido deve ser 'Published' ou 'Rejected'.");
        }

        if (status == MaterialStatus.Rejected && string.IsNullOrWhiteSpace(request.RejectionReason))
        {
            return Results.Problem(
                statusCode: StatusCodes.Status400BadRequest,
                title: "Motivo de Rejeição Obrigatório",
                detail: "O motivo de rejeição (rejectionReason) é obrigatório ao rejeitar um material.");
        }

        try
        {
            var result = await materialService.UpdateMaterialStatusAsync(
                id,
                status,
                request.RejectionReason,
                cancellationToken);

            return Results.Ok(result);
        }
        catch (MaterialNotFoundException ex)
        {
            return Results.Problem(
                statusCode: StatusCodes.Status404NotFound,
                title: "Material Não Encontrado",
                detail: ex.Message);
        }
        catch (ValidationException ex)
        {
            return Results.Problem(
                statusCode: StatusCodes.Status400BadRequest,
                title: "Erro de Validação",
                detail: ex.Message);
        }
    }

    private static bool TryParseModerationStatus(string? value, out MaterialStatus status)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            status = default;
            return false;
        }

        if (Enum.TryParse<MaterialStatus>(value, ignoreCase: true, out status))
        {
            return status == MaterialStatus.Published || status == MaterialStatus.Rejected;
        }

        if (string.Equals(value, "Publicado", StringComparison.OrdinalIgnoreCase))
        {
            status = MaterialStatus.Published;
            return true;
        }

        if (string.Equals(value, "Rejeitado", StringComparison.OrdinalIgnoreCase))
        {
            status = MaterialStatus.Rejected;
            return true;
        }

        status = default;
        return false;
    }
}
