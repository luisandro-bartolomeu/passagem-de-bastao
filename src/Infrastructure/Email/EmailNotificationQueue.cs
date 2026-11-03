using System.Threading.Channels;
using Core.Interfaces;
using Microsoft.Extensions.Logging;

namespace Infrastructure.Email;

public class EmailNotificationQueue : IEmailNotificationQueue
{
    private readonly Channel<EmailMessage> _channel;
    private readonly ILogger<EmailNotificationQueue> _logger;

    public EmailNotificationQueue(ILogger<EmailNotificationQueue> logger, int capacity = 1000)
    {
        _logger = logger;
        var options = new BoundedChannelOptions(capacity)
        {
            FullMode = BoundedChannelFullMode.Wait,
            SingleReader = true,
            SingleWriter = false
        };
        _channel = Channel.CreateBounded<EmailMessage>(options);
    }

    public async ValueTask EnqueueAsync(EmailMessage message, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(message);

        _logger.LogDebug("Enfileirando e-mail para {To} com assunto: {Subject}", message.To, message.Subject);
        await _channel.Writer.WriteAsync(message, cancellationToken);
    }

    public async ValueTask<EmailMessage> DequeueAsync(CancellationToken cancellationToken = default)
    {
        return await _channel.Reader.ReadAsync(cancellationToken);
    }
}
