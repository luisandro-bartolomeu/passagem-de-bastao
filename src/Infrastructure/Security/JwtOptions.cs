namespace Infrastructure.Security;

public class JwtOptions
{
    public const string SectionName = "Jwt";

    public string Issuer { get; set; } = "IgfAcademicRepo";
    public string Audience { get; set; } = "IgfAcademicRepoClient";
    public string SecretKey { get; set; } = "SuperSecretKeyForDevelopmentTesting1234567890!";
    public int AccessTokenExpirationMinutes { get; set; } = 15;
    public int RefreshTokenExpirationDays { get; set; } = 7;
}
