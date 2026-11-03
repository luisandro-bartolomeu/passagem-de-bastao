namespace Core.Exceptions;

public class FileSizeLimitExceededException : Exception
{
    public long MaxAllowedBytes { get; }
    public long? ActualBytes { get; }

    public FileSizeLimitExceededException(long maxAllowedBytes, long? actualBytes = null)
        : base($"O ficheiro excede o tamanho máximo permitido de {maxAllowedBytes / (1024 * 1024)} MB.")
    {
        MaxAllowedBytes = maxAllowedBytes;
        ActualBytes = actualBytes;
    }
}
