namespace Infrastructure.Storage;

public class S3StorageOptions
{
    public const string SectionName = "S3Storage";

    public string ServiceUrl { get; set; } = string.Empty;
    public string BucketName { get; set; } = string.Empty;
    public string AccessKey { get; set; } = string.Empty;
    public string SecretKey { get; set; } = string.Empty;
    public bool UsePublicUrl { get; set; }
    public string PublicBaseUrl { get; set; } = string.Empty;
}
