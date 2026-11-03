using System.Security.Claims;
using Api.Middlewares;
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
            .RequireRateLimiting(RateLimitingExtensions.StrictPolicy)
            .DisableAntiforgery();

        group.MapGet("/", GetMaterials)
            .WithName("GetMaterials");

        group.MapGet("/{id:guid}", GetMaterialById)
            .WithName("GetMaterialById");

        group.MapGet("/{id:guid}/download", DownloadMaterial)
            .WithName("DownloadMaterial");

        group.MapPut("/{id:guid}", UpdateMaterial)
            .WithName("UpdateMaterial")
            .RequireAuthorization("DelegateOrAdmin");

        group.MapDelete("/{id:guid}", DeleteMaterial)
            .WithName("DeleteMaterial")
            .RequireAuthorization("DelegateOrAdmin");

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

    public static async Task<IResult> GetMaterials(
        [FromQuery(Name = "yearLevel")] int? yearLevel,
        [FromQuery(Name = "year")] int? year,
        [FromQuery(Name = "subjectId")] int? subjectId,
        [FromQuery(Name = "materialType")] string? materialTypeStr,
        [FromQuery(Name = "page")] int? page,
        [FromQuery(Name = "pageSize")] int? pageSize,
        IMaterialService materialService,
        CancellationToken cancellationToken)
    {
        var effectiveYear = yearLevel ?? year;
        if (effectiveYear.HasValue && (effectiveYear.Value < 1 || effectiveYear.Value > 4))
        {
            return Results.Problem(
                statusCode: StatusCodes.Status400BadRequest,
                title: "Ano Curricular Inválido",
                detail: "O ano curricular deve estar compreendido entre 1 e 4.");
        }

        MaterialType? materialType = null;
        if (!string.IsNullOrWhiteSpace(materialTypeStr))
        {
            if (!TryParseMaterialType(materialTypeStr, out var parsedType))
            {
                return Results.Problem(
                    statusCode: StatusCodes.Status400BadRequest,
                    title: "Tipo de Material Inválido",
                    detail: "O tipo de material deve ser 'Exam' ('Prova') ou 'LectureNote' ('Sebenta').");
            }
            materialType = parsedType;
        }

        var pageNumber = page.GetValueOrDefault(1);
        if (pageNumber < 1) pageNumber = 1;

        var size = pageSize.GetValueOrDefault(20);
        if (size < 1) size = 20;
        if (size > 50) size = 50;

        var filter = new MaterialQueryFilter(
            YearLevel: effectiveYear,
            SubjectId: subjectId,
            MaterialType: materialType,
            Page: pageNumber,
            PageSize: size
        );

        var result = await materialService.GetPublishedMaterialsAsync(filter, cancellationToken);
        return Results.Ok(result);
    }

    public static async Task<IResult> DownloadMaterial(
        Guid id,
        IMaterialService materialService,
        CancellationToken cancellationToken)
    {
        var download = await materialService.GetMaterialForDownloadAsync(id, cancellationToken);
        if (download == null)
        {
            return Results.Problem(
                statusCode: StatusCodes.Status404NotFound,
                title: "Material Não Encontrado",
                detail: "O material solicitado não existe ou não está publicado para download.");
        }

        return Results.File(
            fileStream: download.Content,
            contentType: download.ContentType,
            fileDownloadName: download.OriginalFileName,
            enableRangeProcessing: true);
    }

    public static async Task<IResult> UpdateMaterial(
        Guid id,
        [FromBody] UpdateMaterialRequest? request,
        ClaimsPrincipal user,
        IMaterialService materialService,
        CancellationToken cancellationToken)
    {
        if (request == null)
        {
            return Results.Problem(
                statusCode: StatusCodes.Status400BadRequest,
                title: "Corpo da Requisição Inválido",
                detail: "Os dados de atualização são obrigatórios.");
        }

        if (string.IsNullOrWhiteSpace(request.Title))
        {
            return Results.Problem(
                statusCode: StatusCodes.Status400BadRequest,
                title: "Título Obrigatório",
                detail: "O título do material não pode ser vazio.");
        }

        if (string.IsNullOrWhiteSpace(request.Description))
        {
            return Results.Problem(
                statusCode: StatusCodes.Status400BadRequest,
                title: "Descrição Obrigatória",
                detail: "A descrição do material não pode ser vazia.");
        }

        if (!TryParseMaterialType(request.MaterialType, out var materialType))
        {
            return Results.Problem(
                statusCode: StatusCodes.Status400BadRequest,
                title: "Tipo de Material Inválido",
                detail: "O tipo de material deve ser 'Exam' ('Prova') ou 'LectureNote' ('Sebenta').");
        }

        if (request.SubjectId <= 0)
        {
            return Results.Problem(
                statusCode: StatusCodes.Status400BadRequest,
                title: "Disciplina Inválida",
                detail: "O identificador da disciplina (subjectId) é obrigatório e deve ser maior que zero.");
        }

        var userIdStr = user.FindFirst(ClaimTypes.NameIdentifier)?.Value;
        if (string.IsNullOrEmpty(userIdStr) || !Guid.TryParse(userIdStr, out var userId))
        {
            return Results.Unauthorized();
        }

        var roleStr = user.FindFirst(ClaimTypes.Role)?.Value;
        if (string.IsNullOrEmpty(roleStr) || !Enum.TryParse<UserRole>(roleStr, ignoreCase: true, out var userRole))
        {
            return Results.Forbid();
        }

        var updateDto = new UpdateMaterialDto(
            Title: request.Title,
            Description: request.Description,
            MaterialType: materialType,
            SubjectId: request.SubjectId,
            PeerHelperName: request.PeerHelperName,
            PeerHelperContact: request.PeerHelperContact
        );

        try
        {
            var updated = await materialService.UpdateMaterialAsync(id, updateDto, userId, userRole, cancellationToken);
            return Results.Ok(updated);
        }
        catch (MaterialNotFoundException ex)
        {
            return Results.Problem(
                statusCode: StatusCodes.Status404NotFound,
                title: "Material Não Encontrado",
                detail: ex.Message);
        }
        catch (ForbiddenOperationException ex)
        {
            return Results.Problem(
                statusCode: StatusCodes.Status403Forbidden,
                title: "Operação Proibida",
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

    public static async Task<IResult> DeleteMaterial(
        Guid id,
        ClaimsPrincipal user,
        IMaterialService materialService,
        CancellationToken cancellationToken)
    {
        var userIdStr = user.FindFirst(ClaimTypes.NameIdentifier)?.Value;
        if (string.IsNullOrEmpty(userIdStr) || !Guid.TryParse(userIdStr, out var userId))
        {
            return Results.Unauthorized();
        }

        var roleStr = user.FindFirst(ClaimTypes.Role)?.Value;
        if (string.IsNullOrEmpty(roleStr) || !Enum.TryParse<UserRole>(roleStr, ignoreCase: true, out var userRole))
        {
            return Results.Forbid();
        }

        try
        {
            await materialService.DeleteMaterialAsync(id, userId, userRole, cancellationToken);
            return Results.NoContent();
        }
        catch (MaterialNotFoundException ex)
        {
            return Results.Problem(
                statusCode: StatusCodes.Status404NotFound,
                title: "Material Não Encontrado",
                detail: ex.Message);
        }
        catch (ForbiddenOperationException ex)
        {
            return Results.Problem(
                statusCode: StatusCodes.Status403Forbidden,
                title: "Operação Proibida",
                detail: ex.Message);
        }
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
