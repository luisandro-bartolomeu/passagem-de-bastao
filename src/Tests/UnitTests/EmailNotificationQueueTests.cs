using Core.Interfaces;
using FluentAssertions;
using Infrastructure.Email;
using Microsoft.Extensions.Logging.Abstractions;

namespace UnitTests;

public class EmailNotificationQueueTests
{
    [Fact]
    public async Task EnqueueAndDequeue_ShouldWorkInFifoOrder()
    {
        var queue = new EmailNotificationQueue(NullLogger<EmailNotificationQueue>.Instance, capacity: 10);
        var msg1 = new EmailMessage("admin1@isutic.ed.ao", "Subject 1", "Body 1");
        var msg2 = new EmailMessage("admin2@isutic.ed.ao", "Subject 2", "Body 2");

        await queue.EnqueueAsync(msg1);
        await queue.EnqueueAsync(msg2);

        var dequeued1 = await queue.DequeueAsync();
        var dequeued2 = await queue.DequeueAsync();

        dequeued1.Should().BeEquivalentTo(msg1);
        dequeued2.Should().BeEquivalentTo(msg2);
    }

    [Fact]
    public async Task EnqueueAsync_WithNullMessage_ShouldThrowArgumentNullException()
    {
        var queue = new EmailNotificationQueue(NullLogger<EmailNotificationQueue>.Instance);

        var act = async () => await queue.EnqueueAsync(null!);

        await act.Should().ThrowAsync<ArgumentNullException>();
    }

    [Fact]
    public async Task DequeueAsync_WithCancelledToken_ShouldThrowOperationCanceledException()
    {
        var queue = new EmailNotificationQueue(NullLogger<EmailNotificationQueue>.Instance);
        using var cts = new CancellationTokenSource();
        cts.Cancel();

        var act = async () => await queue.DequeueAsync(cts.Token);

        await act.Should().ThrowAsync<OperationCanceledException>();
    }
}
