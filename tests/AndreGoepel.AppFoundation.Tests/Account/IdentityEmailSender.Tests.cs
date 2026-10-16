using AndreGoepel.AppFoundation.Hosting;
using AndreGoepel.AppFoundation.MailService;
using AndreGoepel.Marten.Identity.Users;
using NSubstitute;
using Wolverine;

namespace AndreGoepel.AppFoundation.Tests.Account;

public sealed class IdentityEmailSenderTests
{
    private static User AnyUser() => new() { UserName = "alice@example.com" };

    private static (IdentityEmailSender Sender, IMessageBus Bus) Build()
    {
        var bus = Substitute.For<IMessageBus>();
        return (new IdentityEmailSender(bus), bus);
    }

    private static MailMessage Single(IMessageBus bus) =>
        Assert.IsType<MailMessage>(Assert.Single(bus.ReceivedCalls()).GetArguments()[0]);

    #region SendConfirmationLinkAsync

    [Fact]
    public async Task SendConfirmationLinkAsync_SendsToCorrectRecipient()
    {
        // Arrange
        var (sender, bus) = Build();

        // Act
        await sender.SendConfirmationLinkAsync(AnyUser(), "alice@example.com", "http://link");

        // Assert
        Assert.Equal("alice@example.com", Single(bus).Recipient);
    }

    [Fact]
    public async Task SendConfirmationLinkAsync_UsesCorrectSubject()
    {
        // Arrange
        var (sender, bus) = Build();

        // Act
        await sender.SendConfirmationLinkAsync(AnyUser(), "alice@example.com", "http://link");

        // Assert
        Assert.Equal("Confirm your email", Single(bus).Subject);
    }

    [Fact]
    public async Task SendConfirmationLinkAsync_BodyContainsLink()
    {
        // Arrange
        var (sender, bus) = Build();

        // Act
        await sender.SendConfirmationLinkAsync(
            AnyUser(),
            "alice@example.com",
            "http://confirm?token=abc"
        );

        // Assert
        Assert.Contains("http://confirm?token=abc", Single(bus).Body);
    }

    #endregion

    #region SendPasswordResetLinkAsync

    [Fact]
    public async Task SendPasswordResetLinkAsync_SendsToCorrectRecipient()
    {
        // Arrange
        var (sender, bus) = Build();

        // Act
        await sender.SendPasswordResetLinkAsync(AnyUser(), "alice@example.com", "http://reset");

        // Assert
        Assert.Equal("alice@example.com", Single(bus).Recipient);
    }

    [Fact]
    public async Task SendPasswordResetLinkAsync_UsesCorrectSubject()
    {
        // Arrange
        var (sender, bus) = Build();

        // Act
        await sender.SendPasswordResetLinkAsync(AnyUser(), "alice@example.com", "http://reset");

        // Assert
        Assert.Equal("Reset your password", Single(bus).Subject);
    }

    [Fact]
    public async Task SendPasswordResetLinkAsync_BodyContainsLink()
    {
        // Arrange
        var (sender, bus) = Build();

        // Act
        await sender.SendPasswordResetLinkAsync(
            AnyUser(),
            "alice@example.com",
            "http://reset?token=xyz"
        );

        // Assert
        Assert.Contains("http://reset?token=xyz", Single(bus).Body);
    }

    #endregion

    #region SendPasswordResetCodeAsync

    [Fact]
    public async Task SendPasswordResetCodeAsync_SendsToCorrectRecipient()
    {
        // Arrange
        var (sender, bus) = Build();

        // Act
        await sender.SendPasswordResetCodeAsync(AnyUser(), "alice@example.com", "CODE123");

        // Assert
        Assert.Equal("alice@example.com", Single(bus).Recipient);
    }

    [Fact]
    public async Task SendPasswordResetCodeAsync_UsesCorrectSubject()
    {
        // Arrange
        var (sender, bus) = Build();

        // Act
        await sender.SendPasswordResetCodeAsync(AnyUser(), "alice@example.com", "CODE123");

        // Assert
        Assert.Equal("Reset your password", Single(bus).Subject);
    }

    [Fact]
    public async Task SendPasswordResetCodeAsync_BodyContainsCode()
    {
        // Arrange
        var (sender, bus) = Build();

        // Act
        await sender.SendPasswordResetCodeAsync(AnyUser(), "alice@example.com", "CODE123");

        // Assert
        Assert.Contains("CODE123", Single(bus).Body);
    }

    #endregion
}
