using Core.Interfaces;
using FluentAssertions;
using Infrastructure.Email;
using Microsoft.Extensions.Logging.Abstractions;

namespace UnitTests;

public class EmailNotificationBackgroundServiceTests
{
    private class TestEmailSender : IEmailSender
    {
        public List<EmailMessage> SentMessages { get; } = new();
        public bool ShouldThrow { get; set; }
        public TaskCompletionSource<EmailMessage>? MessageReceivedTcs { get; set; }

        public Task SendEmailAsync(EmailMessage message, CancellationToken cancellationToken = default)
        {
            if (ShouldThrow)
            {
                throw new InvalidOperationException("Simulated SMTP network error");
            }

            SentMessages.Add(message);
            MessageReceivedTcs?.TrySetResult(message);
            return Task.CompletedTask;
        }
    }

    [Fact]
    public async Task BackgroundService_ShouldConsumeAndSendEnqueuedEmails()
    {
        var queue = new EmailNotificationQueue(NullLogger<EmailNotificationQueue>.Instance);
        var sender = new TestEmailSender
        {
            MessageReceivedTcs = new TaskCompletionSource<EmailMessage>()
        };

        var backgroundService = new EmailNotificationBackgroundService(
            queue,
            sender,
            NullLogger<EmailNotificationBackgroundService>.Instance);

        using var cts = new CancellationTokenSource();
        var serviceTask = backgroundService.StartAsync(cts.Token);

        var message = new EmailMessage("admin@isutic.ed.ao", "Novo Material", "Corpo do e-mail");
        await queue.EnqueueAsync(message, cts.Token);

        // Wait for background service to process the message
        var completedTask = await Task.WhenAny(sender.MessageReceivedTcs.Task, Task.Delay(3000));
        completedTask.Should().Be(sender.MessageReceivedTcs.Task, "o e-mail deveria ser consumido pelo background service dentro do tempo limite.");

        var receivedMessage = await sender.MessageReceivedTcs.Task;
        receivedMessage.Should().BeEquivalentTo(message);
        sender.SentMessages.Should().ContainSingle().Which.Should().BeEquivalentTo(message);

        // Stop the background service
        await cts.CancelAsync();
        await backgroundService.StopAsync(CancellationToken.None);
        await serviceTask;
    }

    [Fact]
    public async Task BackgroundService_WhenSenderThrows_ShouldNotCrashAndContinueProcessing()
    {
        var queue = new EmailNotificationQueue(NullLogger<EmailNotificationQueue>.Instance);
        var sender = new TestEmailSender();

        var backgroundService = new EmailNotificationBackgroundService(
            queue,
            sender,
            NullLogger<EmailNotificationBackgroundService>.Instance);

        using var cts = new CancellationTokenSource();
        var serviceTask = backgroundService.StartAsync(cts.Token);

        // First message will fail
        sender.ShouldThrow = true;
        var failingMessage = new EmailMessage("fail@isutic.ed.ao", "Failing Email", "Body");
        await queue.EnqueueAsync(failingMessage, cts.Token);

        // Small delay to let the background service attempt and handle the error
        await Task.Delay(200);

        // Second message should succeed
        sender.ShouldThrow = false;
        sender.MessageReceivedTcs = new TaskCompletionSource<EmailMessage>();
        var successMessage = new EmailMessage("success@isutic.ed.ao", "Success Email", "Body");
        await queue.EnqueueAsync(successMessage, cts.Token);

        var completedTask = await Task.WhenAny(sender.MessageReceivedTcs.Task, Task.Delay(3000));
        completedTask.Should().Be(sender.MessageReceivedTcs.Task, "o background service deve continuar a funcionar após uma falha transitória.");

        sender.SentMessages.Should().ContainSingle().Which.Should().BeEquivalentTo(successMessage);

        await cts.CancelAsync();
        await backgroundService.StopAsync(CancellationToken.None);
        await serviceTask;
    }

    [Fact]
    public async Task BackgroundService_ShouldStopGracefullyOnCancellation()
    {
        var queue = new EmailNotificationQueue(NullLogger<EmailNotificationQueue>.Instance);
        var sender = new TestEmailSender();

        var backgroundService = new EmailNotificationBackgroundService(
            queue,
            sender,
            NullLogger<EmailNotificationBackgroundService>.Instance);

        using var cts = new CancellationTokenSource();
        var startTask = backgroundService.StartAsync(cts.Token);

        await cts.CancelAsync();
        await backgroundService.StopAsync(CancellationToken.None);

        startTask.IsCompleted.Should().BeTrue();
    }
}
