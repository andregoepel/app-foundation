using AndreGoepel.AppFoundation.MailService;
using Microsoft.Extensions.Logging.Abstractions;
using NSubstitute;
using Wolverine;

namespace AndreGoepel.AppFoundation.MailService.Tests;

public class SendEmailMessageHandlerTests
{
    [Fact]
    public async Task Handle_LocalOrigin_ForwardsAllFieldsToEmailSender()
    {
        // Arrange
        var sender = Substitute.For<IEmailSender>();
        var handler = new SendEmailMessageHandler(
            sender,
            NullLogger<SendEmailMessageHandler>.Instance
        );
        var message = new MailMessage("bob@example.com", "Hello", "World");

        // Act
        await handler.Handle(message, LocalEnvelope(message));

        // Assert
        await sender.Received(1).SendAsync("bob@example.com", "Hello", "World");
    }

    [Fact]
    public async Task Handle_LocalOrigin_DelegatesToEmailSender_ExactlyOnce()
    {
        // Arrange
        var sender = Substitute.For<IEmailSender>();
        var handler = new SendEmailMessageHandler(
            sender,
            NullLogger<SendEmailMessageHandler>.Instance
        );
        var message = new MailMessage("a@b.com", "s", "b");

        // Act
        await handler.Handle(message, LocalEnvelope(message));

        // Assert
        await sender.ReceivedWithAnyArgs(1).SendAsync(default!, default!, default!);
    }

    [Fact]
    public async Task Handle_ExternalOrigin_DropsMessageWithoutSending()
    {
        // Arrange — a MailMessage that arrived over an external transport.
        var sender = Substitute.For<IEmailSender>();
        var handler = new SendEmailMessageHandler(
            sender,
            NullLogger<SendEmailMessageHandler>.Instance
        );
        var message = new MailMessage("attacker@evil.example", "Spam", "Body");
        var external = new Envelope(message) { Destination = new Uri("rabbitmq://queue/mail") };

        // Act
        await handler.Handle(message, external);

        // Assert — nothing was sent.
        await sender.DidNotReceiveWithAnyArgs().SendAsync(default!, default!, default!);
    }

    [Theory]
    [InlineData("local://mail", true)]
    [InlineData("local://durable/mail", true)]
    [InlineData("rabbitmq://queue/mail", false)]
    [InlineData("tcp://10.0.0.5:5000", false)]
    [InlineData("sqs://mail", false)]
    public void IsLocalOrigin_ClassifiesByScheme(string destination, bool expected)
    {
        // Act / Assert
        Assert.Equal(expected, SendEmailMessageHandler.IsLocalOrigin(new Uri(destination)));
    }

    [Fact]
    public void IsLocalOrigin_NullDestination_IsTreatedAsLocal()
    {
        // Act / Assert — direct in-process invocation must never be blocked.
        Assert.True(SendEmailMessageHandler.IsLocalOrigin(null));
    }

    private static Envelope LocalEnvelope(object message) =>
        new(message) { Destination = new Uri("local://mail") };
}
