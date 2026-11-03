namespace Core.Interfaces;

public record FileValidationResult
{
    public bool IsValid { get; init; }
    public string? ErrorMessage { get; init; }
    public string? NormalizedExtension { get; init; }
    public string? DetectedContentType { get; init; }

    public static FileValidationResult Success(string extension, string contentType) =>
        new()
        {
            IsValid = true,
            NormalizedExtension = extension,
            DetectedContentType = contentType
        };

    public static FileValidationResult Failure(string errorMessage) =>
        new()
        {
            IsValid = false,
            ErrorMessage = errorMessage
        };
}

public interface IFileValidator
{
    long MaxFileSizeBytes { get; }
    FileValidationResult Validate(Stream stream, string fileName);
}
