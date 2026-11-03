namespace Infrastructure.Security;

public class RateLimitOptions
{
    public const string SectionName = "RateLimiting";

    public int GeneralPermitLimit { get; set; } = 100;
    public int GeneralWindowSeconds { get; set; } = 60;

    public int StrictPermitLimit { get; set; } = 10;
    public int StrictWindowSeconds { get; set; } = 60;
}
