using AndreGoepel.MembersArea.MailService;
using NSubstitute;

namespace AndreGoepel.MembersArea.MailService.Tests;

public class SendEmailMessageHandlerTests
{
    [Fact]
    public async Task Handle_ForwardsAllFieldsToEmailSender()
    {
        var sender = Substitute.For<IEmailSender>();
        var handler = new SendEmailMessageHandler(sender);
        var message = new MailMessage("bob@example.com", "Hello", "World");

        await handler.Handle(message);

        await sender.Received(1).SendAsync("bob@example.com", "Hello", "World");
    }

    [Fact]
    public async Task Handle_DelegatesToEmailSender_ExactlyOnce()
    {
        var sender = Substitute.For<IEmailSender>();
        var handler = new SendEmailMessageHandler(sender);

        await handler.Handle(new MailMessage("a@b.com", "s", "b"));

        await sender.ReceivedWithAnyArgs(1).SendAsync(default!, default!, default!);
    }
}
