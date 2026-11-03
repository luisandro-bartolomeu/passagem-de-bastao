using Core.Exceptions;
using Core.Interfaces;
using Microsoft.Extensions.Options;

namespace Infrastructure.Storage;

public class LocalFileStorageService : IFileStorageService
{
    private readonly IFileValidator _fileValidator;
    private readonly LocalStorageOptions _options;

    public LocalFileStorageService(
        IFileValidator fileValidator,
        IOptions<LocalStorageOptions> options)
    {
        _fileValidator = fileValidator;
        _options = options.Value;
    }

    public async Task<FileStorageResult> UploadAsync(
        Stream content,
        string fileName,
        string contentType,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(content);
        ArgumentException.ThrowIfNullOrWhiteSpace(fileName);

        var validation = _fileValidator.Validate(content, fileName);
        if (!validation.IsValid)
        {
            if ((content.CanSeek && content.Length > _fileValidator.MaxFileSizeBytes) ||
                (validation.ErrorMessage != null && validation.ErrorMessage.Contains("50 MB", StringComparison.OrdinalIgnoreCase)))
            {
                throw new FileSizeLimitExceededException(
                    _fileValidator.MaxFileSizeBytes,
                    content.CanSeek ? content.Length : null);
            }

            throw new InvalidFileFormatException(validation.ErrorMessage ?? "Ficheiro inválido.");
        }

        var ext = validation.NormalizedExtension ?? Path.GetExtension(fileName).ToLowerInvariant();
        var dateFolder = DateTime.UtcNow.ToString("yyyy/MM");
        var safeFileName = $"{Guid.NewGuid():N}{ext}";
        var storageKey = $"{dateFolder}/{safeFileName}";

        var targetDirectory = Path.Combine(_options.StoragePath, dateFolder);
        Directory.CreateDirectory(targetDirectory);

        var fullFilePath = Path.Combine(_options.StoragePath, storageKey);

        if (content.CanSeek)
        {
            content.Position = 0;
        }

        await using (var fileStream = new FileStream(fullFilePath, FileMode.Create, FileAccess.Write, FileShare.None))
        {
            await content.CopyToAsync(fileStream, cancellationToken);
        }

        var fileInfo = new FileInfo(fullFilePath);
        var finalContentType = validation.DetectedContentType ?? contentType;

        return new FileStorageResult(
            StorageKey: storageKey.Replace('\\', '/'),
            OriginalFileName: fileName,
            ContentType: finalContentType,
            FileSizeBytes: fileInfo.Length
        );
    }

    public Task<FileDownloadResult?> DownloadAsync(
        string storageKey,
        CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(storageKey))
        {
            return Task.FromResult<FileDownloadResult?>(null);
        }

        var fullFilePath = Path.Combine(_options.StoragePath, storageKey);
        if (!File.Exists(fullFilePath))
        {
            return Task.FromResult<FileDownloadResult?>(null);
        }

        var extension = Path.GetExtension(fullFilePath).ToLowerInvariant();
        var contentType = extension switch
        {
            ".pdf" => "application/pdf",
            ".png" => "image/png",
            ".jpg" or ".jpeg" => "image/jpeg",
            ".ppt" => "application/vnd.ms-powerpoint",
            ".pptx" => "application/vnd.openxmlformats-officedocument.presentationml.presentation",
            _ => "application/octet-stream"
        };

        var stream = new FileStream(fullFilePath, FileMode.Open, FileAccess.Read, FileShare.Read);
        var result = new FileDownloadResult(stream, contentType, Path.GetFileName(fullFilePath));

        return Task.FromResult<FileDownloadResult?>(result);
    }

    public Task<bool> DeleteAsync(
        string storageKey,
        CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(storageKey))
        {
            return Task.FromResult(false);
        }

        var fullFilePath = Path.Combine(_options.StoragePath, storageKey);
        if (!File.Exists(fullFilePath))
        {
            return Task.FromResult(false);
        }

        try
        {
            File.Delete(fullFilePath);
            return Task.FromResult(true);
        }
        catch
        {
            return Task.FromResult(false);
        }
    }

    public Task<string> GetFileUrlAsync(
        string storageKey,
        TimeSpan? expiry = null,
        CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(storageKey);

        var normalizedKey = storageKey.Replace('\\', '/').TrimStart('/');
        var baseUrl = _options.BaseUrl.TrimEnd('/');
        var url = $"{baseUrl}/{normalizedKey}";

        return Task.FromResult(url);
    }
}
