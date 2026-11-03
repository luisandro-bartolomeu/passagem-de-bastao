namespace Core.Interfaces;

public record FileStorageResult(
    string StorageKey,
    string OriginalFileName,
    string ContentType,
    long FileSizeBytes
);

public record FileDownloadResult(
    Stream Content,
    string ContentType,
    string FileName
);

public interface IFileStorageService
{
    Task<FileStorageResult> UploadAsync(
        Stream content,
        string fileName,
        string contentType,
        CancellationToken cancellationToken = default);

    Task<FileDownloadResult?> DownloadAsync(
        string storageKey,
        CancellationToken cancellationToken = default);

    Task<bool> DeleteAsync(
        string storageKey,
        CancellationToken cancellationToken = default);

    Task<string> GetFileUrlAsync(
        string storageKey,
        TimeSpan? expiry = null,
        CancellationToken cancellationToken = default);
}
