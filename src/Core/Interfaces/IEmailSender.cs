namespace Core.Interfaces;

public record EmailMessage(
    string To,
    string Subject,
    string Body,
    bool IsHtml = true
);

public interface IEmailSender
{
    Task SendEmailAsync(EmailMessage message, CancellationToken cancellationToken = default);
}
