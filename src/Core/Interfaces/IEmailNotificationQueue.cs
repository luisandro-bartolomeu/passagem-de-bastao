namespace Core.Interfaces;

public interface IEmailNotificationQueue
{
    ValueTask EnqueueAsync(EmailMessage message, CancellationToken cancellationToken = default);
    ValueTask<EmailMessage> DequeueAsync(CancellationToken cancellationToken = default);
}
