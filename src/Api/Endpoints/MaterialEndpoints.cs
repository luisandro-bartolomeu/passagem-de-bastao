using System.Security.Claims;
using Core.DTOs;
using Core.Enums;
using Core.Exceptions;
using Core.Interfaces;
using Microsoft.AspNetCore.Mvc;

namespace Api.Endpoints;

public static class MaterialEndpoints
{
    public static IEndpointRouteBuilder MapMaterialEndpoints(this IEndpointRouteBuilder app)
    {
        var group = app.MapGroup("/api/v1/materials")
            .WithTags("Materials");

        group.MapPost("/", SubmitMaterial)
            .WithName("SubmitMaterial")
            .RequireAuthorization("DelegateOrAdmin")
            .DisableAntiforgery();

        group.MapGet("/{id:guid}", GetMaterialById)
            .WithName("GetMaterialById");

        return app;
    }

    public static async Task<IResult> SubmitMaterial(
        HttpRequest request,
        ClaimsPrincipal user,
        IMaterialService materialService,
        CancellationToken cancellationToken)
    {
        if (!request.HasFormContentType)
        {
            return Results.Problem(
                statusCode: StatusCodes.Status400BadRequest,
                title: "Content-Type Inválido",
                detail: "A submissão deve ser efetuada no formato multipart/form-data.");
        }

        var form = await request.ReadFormAsync(cancellationToken);

        // 1. Validate attached file
        var file = form.Files.GetFile("file") ?? form.Files.FirstOrDefault();
        if (file == null || file.Length == 0)
        {
            return Results.Problem(
                statusCode: StatusCodes.Status400BadRequest,
                title: "Ficheiro Obrigatório",
                detail: "É obrigatório anexar um ficheiro para a submissão do material.");
        }

        if (file.Length > 50 * 1024 * 1024)
        {
            return Results.Problem(
                statusCode: StatusCodes.Status413PayloadTooLarge,
                title: "Payload Too Large",
                detail: "O ficheiro excede o limite máximo permitido de 50 MB.");
        }

        // 2. Validate form fields
        var title = form["title"].ToString();
        if (string.IsNullOrWhiteSpace(title))
        {
            return Results.Problem(
                statusCode: StatusCodes.Status400BadRequest,
                title: "Título Obrigatório",
                detail: "O título do material não pode ser vazio.");
        }

        var description = form["description"].ToString();
        if (string.IsNullOrWhiteSpace(description))
        {
            return Results.Problem(
                statusCode: StatusCodes.Status400BadRequest,
                title: "Descrição Obrigatória",
                detail: "A descrição do material não pode ser vazia.");
        }

        var subjectIdStr = form["subjectId"].ToString();
        if (!int.TryParse(subjectIdStr, out var subjectId))
        {
            return Results.Problem(
                statusCode: StatusCodes.Status400BadRequest,
                title: "Disciplina Inválida",
                detail: "O identificador da disciplina (subjectId) é obrigatório e deve ser um número inteiro.");
        }

        var materialTypeStr = form["materialType"].ToString();
        if (!TryParseMaterialType(materialTypeStr, out var materialType))
        {
            return Results.Problem(
                statusCode: StatusCodes.Status400BadRequest,
                title: "Tipo de Material Inválido",
                detail: "O tipo de material deve ser 'Exam' ('Prova') ou 'LectureNote' ('Sebenta').");
        }

        var peerHelperName = form["peerHelperName"].ToString();
        var peerHelperContact = form["peerHelperContact"].ToString();

        // 3. Extract user id from token claims
        var userIdStr = user.FindFirst(ClaimTypes.NameIdentifier)?.Value;
        if (string.IsNullOrEmpty(userIdStr) || !Guid.TryParse(userIdStr, out var userId))
        {
            return Results.Unauthorized();
        }

        // 4. Invoke domain service
        try
        {
            await using var stream = file.OpenReadStream();
            var dto = new SubmitMaterialDto(
                Title: title,
                Description: description,
                MaterialType: materialType,
                SubjectId: subjectId,
                PeerHelperName: string.IsNullOrWhiteSpace(peerHelperName) ? null : peerHelperName,
                PeerHelperContact: string.IsNullOrWhiteSpace(peerHelperContact) ? null : peerHelperContact,
                FileStream: stream,
                FileName: file.FileName,
                ContentType: file.ContentType,
                FileSizeBytes: file.Length,
                UploadedByUserId: userId
            );

            var result = await materialService.SubmitMaterialAsync(dto, cancellationToken);

            return Results.Created($"/api/v1/materials/{result.Id}", result);
        }
        catch (FileSizeLimitExceededException ex)
        {
            return Results.Problem(
                statusCode: StatusCodes.Status413PayloadTooLarge,
                title: "Payload Too Large",
                detail: ex.Message);
        }
        catch (InvalidFileFormatException ex)
        {
            return Results.Problem(
                statusCode: StatusCodes.Status400BadRequest,
                title: "Formato de Ficheiro Inválido",
                detail: ex.Message);
        }
        catch (SubjectNotFoundException ex)
        {
            return Results.Problem(
                statusCode: StatusCodes.Status400BadRequest,
                title: "Disciplina Não Encontrada",
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

    public static async Task<IResult> GetMaterialById(
        Guid id,
        IMaterialService materialService,
        CancellationToken cancellationToken)
    {
        var material = await materialService.GetMaterialByIdAsync(id, cancellationToken);
        return material == null ? Results.NotFound() : Results.Ok(material);
    }

    private static bool TryParseMaterialType(string value, out MaterialType materialType)
    {
        if (Enum.TryParse<MaterialType>(value, ignoreCase: true, out materialType))
        {
            return true;
        }

        if (string.Equals(value, "Prova", StringComparison.OrdinalIgnoreCase))
        {
            materialType = MaterialType.Exam;
            return true;
        }

        if (string.Equals(value, "Sebenta", StringComparison.OrdinalIgnoreCase) ||
            string.Equals(value, "Conteudo", StringComparison.OrdinalIgnoreCase) ||
            string.Equals(value, "Conteúdo", StringComparison.OrdinalIgnoreCase))
        {
            materialType = MaterialType.LectureNote;
            return true;
        }

        materialType = default;
        return false;
    }
}
