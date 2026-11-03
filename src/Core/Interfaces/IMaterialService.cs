using Core.DTOs;
using Core.Enums;

namespace Core.Interfaces;

public interface IMaterialService
{
    Task<MaterialResponseDto> SubmitMaterialAsync(
        SubmitMaterialDto dto,
        CancellationToken cancellationToken = default);

    Task<MaterialResponseDto?> GetMaterialByIdAsync(
        Guid id,
        CancellationToken cancellationToken = default);

    Task<MaterialResponseDto> UpdateMaterialAsync(
        Guid id,
        UpdateMaterialDto dto,
        Guid currentUserId,
        UserRole currentUserRole,
        CancellationToken cancellationToken = default);

    Task DeleteMaterialAsync(
        Guid id,
        Guid currentUserId,
        UserRole currentUserRole,
        CancellationToken cancellationToken = default);

    Task<MaterialResponseDto> UpdateMaterialStatusAsync(
        Guid id,
        MaterialStatus newStatus,
        string? rejectionReason,
        CancellationToken cancellationToken = default);
}
