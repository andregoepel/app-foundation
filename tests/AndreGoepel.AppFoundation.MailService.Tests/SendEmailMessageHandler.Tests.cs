using AndreGoepel.AppFoundation.MailService;
using NSubstitute;

namespace AndreGoepel.AppFoundation.MailService.Tests;

public class SendEmailMessageHandlerTests
{
    [Fact]
    public async Task Handle_ForwardsAllFieldsToEmailSender()
    {
        // Arrange
        var sender = Substitute.For<IEmailSender>();
        var handler = new SendEmailMessageHandler(sender);
        var message = new MailMessage("bob@example.com", "Hello", "World");

        // Act
        await handler.Handle(message);

        // Assert
        await sender.Received(1).SendAsync("bob@example.com", "Hello", "World");
    }

    [Fact]
    public async Task Handle_DelegatesToEmailSender_ExactlyOnce()
    {
        // Arrange
        var sender = Substitute.For<IEmailSender>();
        var handler = new SendEmailMessageHandler(sender);

        // Act
        await handler.Handle(new MailMessage("a@b.com", "s", "b"));

        // Assert
        await sender.ReceivedWithAnyArgs(1).SendAsync(default!, default!, default!);
    }
}
