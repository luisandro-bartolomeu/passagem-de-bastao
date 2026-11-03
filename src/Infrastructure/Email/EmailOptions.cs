namespace Infrastructure.Email;

public class EmailOptions
{
    public const string SectionName = "Email";

    public string Host { get; set; } = "localhost";
    public int Port { get; set; } = 587;
    public string Username { get; set; } = string.Empty;
    public string Password { get; set; } = string.Empty;
    public string SenderEmail { get; set; } = "noreply@isutic.ed.ao";
    public string SenderName { get; set; } = "Repositório Académico IGF";
    public string AdminEmail { get; set; } = "admin@isutic.ed.ao";
    public bool EnableSsl { get; set; } = false;
    public bool UseMock { get; set; } = true;
}
