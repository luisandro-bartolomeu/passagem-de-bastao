using Core.DTOs;

namespace Core.Interfaces;

public interface IMaterialService
{
    Task<MaterialResponseDto> SubmitMaterialAsync(
        SubmitMaterialDto dto,
        CancellationToken cancellationToken = default);

    Task<MaterialResponseDto?> GetMaterialByIdAsync(
        Guid id,
        CancellationToken cancellationToken = default);
}
