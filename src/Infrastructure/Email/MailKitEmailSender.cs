using Core.Interfaces;
using MailKit.Net.Smtp;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using MimeKit;

namespace Infrastructure.Email;

public class MailKitEmailSender : IEmailSender
{
    private readonly EmailOptions _options;
    private readonly ILogger<MailKitEmailSender> _logger;

    public MailKitEmailSender(
        IOptions<EmailOptions> options,
        ILogger<MailKitEmailSender> logger)
    {
        _options = options.Value;
        _logger = logger;
    }

    public async Task SendEmailAsync(EmailMessage message, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(message);

        if (_options.UseMock)
        {
            _logger.LogInformation(
                "[MOCK EMAIL] Para: {To} | De: {From} | Assunto: {Subject}\nCorpo:\n{Body}",
                message.To,
                _options.SenderEmail,
                message.Subject,
                message.Body);
            return;
        }

        var mimeMessage = new MimeMessage();
        mimeMessage.From.Add(new MailboxAddress(_options.SenderName, _options.SenderEmail));
        mimeMessage.To.Add(MailboxAddress.Parse(message.To));
        mimeMessage.Subject = message.Subject;

        var bodyBuilder = new BodyBuilder();
        if (message.IsHtml)
        {
            bodyBuilder.HtmlBody = message.Body;
        }
        else
        {
            bodyBuilder.TextBody = message.Body;
        }

        mimeMessage.Body = bodyBuilder.ToMessageBody();

        using var client = new SmtpClient();
        await client.ConnectAsync(_options.Host, _options.Port, _options.EnableSsl, cancellationToken);

        if (!string.IsNullOrWhiteSpace(_options.Username))
        {
            await client.AuthenticateAsync(_options.Username, _options.Password, cancellationToken);
        }

        await client.SendAsync(mimeMessage, cancellationToken);
        await client.DisconnectAsync(true, cancellationToken);
    }
}
