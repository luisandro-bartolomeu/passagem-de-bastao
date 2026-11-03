using Core.Enums;

namespace Core.DTOs;

public record SubmitMaterialDto(
    string Title,
    string Description,
    MaterialType MaterialType,
    int SubjectId,
    string? PeerHelperName,
    string? PeerHelperContact,
    Stream FileStream,
    string FileName,
    string ContentType,
    long FileSizeBytes,
    Guid UploadedByUserId
);

public record MaterialResponseDto(
    Guid Id,
    int SubjectId,
    string SubjectName,
    Guid UploadedByUserId,
    string UploadedByUserName,
    string Title,
    string Description,
    string MaterialType,
    string FileStorageKey,
    string OriginalFileName,
    string ContentType,
    long FileSizeBytes,
    string Status,
    string? PeerHelperName,
    string? PeerHelperContact,
    DateTime CreatedAt,
    DateTime? UpdatedAt
);

public record UpdateMaterialDto(
    string Title,
    string Description,
    MaterialType MaterialType,
    int SubjectId,
    string? PeerHelperName,
    string? PeerHelperContact
);

public record UpdateMaterialRequest(
    string Title,
    string Description,
    string MaterialType,
    int SubjectId,
    string? PeerHelperName,
    string? PeerHelperContact
);
