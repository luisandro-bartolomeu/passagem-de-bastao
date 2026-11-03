using Core.Interfaces;

namespace Infrastructure.Storage;

public class FileValidator : IFileValidator
{
    public const long DefaultMaxFileSizeBytes = 50 * 1024 * 1024; // 50 MB

    public long MaxFileSizeBytes => DefaultMaxFileSizeBytes;

    // Allowed extensions and corresponding MIME types
    private static readonly Dictionary<string, string> AllowedExtensions = new(StringComparer.OrdinalIgnoreCase)
    {
        { ".pdf", "application/pdf" },
        { ".png", "image/png" },
        { ".jpg", "image/jpeg" },
        { ".jpeg", "image/jpeg" },
        { ".ppt", "application/vnd.ms-powerpoint" },
        { ".pptx", "application/vnd.openxmlformats-officedocument.presentationml.presentation" }
    };

    // Magic numbers signatures
    private static readonly byte[] PdfMagic = [0x25, 0x50, 0x44, 0x46]; // %PDF
    private static readonly byte[] PngMagic = [0x89, 0x50, 0x4E, 0x47, 0x0D, 0x0A, 0x1A, 0x0A];
    private static readonly byte[] JpegMagic = [0xFF, 0xD8, 0xFF];
    private static readonly byte[] PptMagic = [0xD0, 0xCF, 0x11, 0xE0, 0xA1, 0xB1, 0x1A, 0xE1]; // OLE2
    private static readonly byte[] PptxMagic = [0x50, 0x4B, 0x03, 0x04]; // PK zip local file header

    // Known dangerous signatures (e.g. Windows PE/DOS executable, ELF)
    private static readonly byte[] ExeMagic = [0x4D, 0x5A]; // MZ
    private static readonly byte[] ElfMagic = [0x7F, 0x45, 0x4C, 0x46]; // .ELF

    public FileValidationResult Validate(Stream stream, string fileName)
    {
        ArgumentNullException.ThrowIfNull(stream);

        if (string.IsNullOrWhiteSpace(fileName))
        {
            return FileValidationResult.Failure("O nome do ficheiro não pode ser vazio.");
        }

        var extension = Path.GetExtension(fileName).ToLowerInvariant();
        if (string.IsNullOrEmpty(extension) || !AllowedExtensions.TryGetValue(extension, out var contentType))
        {
            return FileValidationResult.Failure(
                $"Extensão '{extension}' não permitida. Apenas ficheiros .pdf, .png, .jpg, .ppt e .pptx são suportados.");
        }

        if (stream.CanSeek)
        {
            if (stream.Length > MaxFileSizeBytes)
            {
                return FileValidationResult.Failure(
                    $"O ficheiro excede o tamanho máximo permitido de 50 MB (tamanho atual: {stream.Length} bytes).");
            }

            if (stream.Length < 4)
            {
                return FileValidationResult.Failure("O ficheiro está vazio ou corrompido.");
            }
        }

        // Read header bytes while preserving stream position
        var originalPosition = stream.CanSeek ? stream.Position : 0;
        var header = new byte[16];
        int bytesRead;

        try
        {
            if (stream.CanSeek)
            {
                stream.Position = 0;
            }

            bytesRead = stream.Read(header, 0, header.Length);
        }
        finally
        {
            if (stream.CanSeek)
            {
                stream.Position = originalPosition;
            }
        }

        if (bytesRead < 4)
        {
            return FileValidationResult.Failure("Não foi possível ler os cabeçalhos binários do ficheiro.");
        }

        // Check for malicious executable signatures
        if (StartsWith(header, bytesRead, ExeMagic) || StartsWith(header, bytesRead, ElfMagic))
        {
            return FileValidationResult.Failure(
                "Ficheiro executável malicioso detetado através de verificação binária (magic bytes).");
        }

        // Check magic bytes for the declared extension
        var isSignatureValid = extension switch
        {
            ".pdf" => StartsWith(header, bytesRead, PdfMagic),
            ".png" => StartsWith(header, bytesRead, PngMagic),
            ".jpg" or ".jpeg" => StartsWith(header, bytesRead, JpegMagic),
            ".ppt" => StartsWith(header, bytesRead, PptMagic),
            ".pptx" => StartsWith(header, bytesRead, PptxMagic),
            _ => false
        };

        if (!isSignatureValid)
        {
            return FileValidationResult.Failure(
                $"Os magic bytes binários não correspondem à extensão declarada '{extension}'. Possível extensão adulterada.");
        }

        return FileValidationResult.Success(extension, contentType);
    }

    private static bool StartsWith(byte[] source, int sourceLength, byte[] pattern)
    {
        if (sourceLength < pattern.Length)
        {
            return false;
        }

        for (var i = 0; i < pattern.Length; i++)
        {
            if (source[i] != pattern[i])
            {
                return false;
            }
        }

        return true;
    }
}
