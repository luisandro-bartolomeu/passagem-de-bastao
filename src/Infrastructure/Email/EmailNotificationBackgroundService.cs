using Core.Interfaces;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;

namespace Infrastructure.Email;

public class EmailNotificationBackgroundService : BackgroundService
{
    private readonly IEmailNotificationQueue _queue;
    private readonly IEmailSender _emailSender;
    private readonly ILogger<EmailNotificationBackgroundService> _logger;

    public EmailNotificationBackgroundService(
        IEmailNotificationQueue queue,
        IEmailSender emailSender,
        ILogger<EmailNotificationBackgroundService> logger)
    {
        _queue = queue;
        _emailSender = emailSender;
        _logger = logger;
    }

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        _logger.LogInformation("Serviço em segundo plano de despacho de e-mails iniciado.");

        while (!stoppingToken.IsCancellationRequested)
        {
            try
            {
                var message = await _queue.DequeueAsync(stoppingToken);

                try
                {
                    _logger.LogInformation("Despachando e-mail assíncrono para {To}: {Subject}", message.To, message.Subject);
                    await _emailSender.SendEmailAsync(message, stoppingToken);
                    _logger.LogInformation("E-mail para {To} enviado com sucesso.", message.To);
                }
                catch (Exception ex) when (!stoppingToken.IsCancellationRequested)
                {
                    _logger.LogError(ex, "Erro ao enviar e-mail para {To} com assunto: {Subject}", message.To, message.Subject);
                    // Failure handled gracefully without terminating the queue processing loop
                }
            }
            catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
            {
                break;
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Erro inesperado no loop do EmailNotificationBackgroundService.");
            }
        }

        _logger.LogInformation("Serviço em segundo plano de despacho de e-mails encerrado.");
    }
}
