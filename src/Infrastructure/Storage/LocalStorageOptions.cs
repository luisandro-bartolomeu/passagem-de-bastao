namespace Infrastructure.Storage;

public class LocalStorageOptions
{
    public const string SectionName = "LocalStorage";

    public string StoragePath { get; set; } = "uploads";
    public string BaseUrl { get; set; } = "http://localhost:5000/uploads";
}
