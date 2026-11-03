using Core.Interfaces;
using FluentAssertions;
using Infrastructure.Email;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;

namespace UnitTests;

public class MailKitEmailSenderTests
{
    [Fact]
    public async Task SendEmailAsync_InMockMode_ShouldCompleteSuccessfully()
    {
        var options = Options.Create(new EmailOptions
        {
            UseMock = true,
            SenderEmail = "noreply@isutic.ed.ao",
            SenderName = "IGF Repo"
        });

        var sender = new MailKitEmailSender(options, NullLogger<MailKitEmailSender>.Instance);
        var message = new EmailMessage("admin@isutic.ed.ao", "Assunto Teste", "<p>Corpo</p>", IsHtml: true);

        var act = async () => await sender.SendEmailAsync(message);

        await act.Should().NotThrowAsync();
    }

    [Fact]
    public async Task SendEmailAsync_WithNullMessage_ShouldThrowArgumentNullException()
    {
        var options = Options.Create(new EmailOptions { UseMock = true });
        var sender = new MailKitEmailSender(options, NullLogger<MailKitEmailSender>.Instance);

        var act = async () => await sender.SendEmailAsync(null!);

        await act.Should().ThrowAsync<ArgumentNullException>();
    }
}
