using AndreGoepel.Marten.Identity.Users;
using AndreGoepel.AppFoundation.Components.Account;
using AndreGoepel.AppFoundation.MailService;
using NSubstitute;
using Wolverine;

namespace AndreGoepel.AppFoundation.Tests.Account;

public class IdentityEmailSenderTests
{
    private static User AnyUser() => new() { UserName = "alice@example.com" };

    private static (IdentityEmailSender Sender, List<object> Sent) Build()
    {
        var bus = Substitute.For<IMessageBus>();
        var sent = new List<object>();
        bus.SendAsync(Arg.Do<object>(m => sent.Add(m)));
        return (new IdentityEmailSender(bus), sent);
    }

    private static MailMessage Single(List<object> sent) =>
        Assert.IsType<MailMessage>(Assert.Single(sent));

    #region SendConfirmationLinkAsync

    [Fact]
    public async Task SendConfirmationLinkAsync_SendsToCorrectRecipient()
    {
        // Arrange
        var (sender, sent) = Build();

        // Act
        await sender.SendConfirmationLinkAsync(AnyUser(), "alice@example.com", "http://link");

        // Assert
        Assert.Equal("alice@example.com", Single(sent).Recipient);
    }

    [Fact]
    public async Task SendConfirmationLinkAsync_UsesCorrectSubject()
    {
        // Arrange
        var (sender, sent) = Build();

        // Act
        await sender.SendConfirmationLinkAsync(AnyUser(), "alice@example.com", "http://link");

        // Assert
        Assert.Equal("Confirm your email", Single(sent).Subject);
    }

    [Fact]
    public async Task SendConfirmationLinkAsync_BodyContainsLink()
    {
        // Arrange
        var (sender, sent) = Build();

        // Act
        await sender.SendConfirmationLinkAsync(
            AnyUser(),
            "alice@example.com",
            "http://confirm?token=abc"
        );

        // Assert
        Assert.Contains("http://confirm?token=abc", Single(sent).Body);
    }

    #endregion

    #region SendPasswordResetLinkAsync

    [Fact]
    public async Task SendPasswordResetLinkAsync_SendsToCorrectRecipient()
    {
        // Arrange
        var (sender, sent) = Build();

        // Act
        await sender.SendPasswordResetLinkAsync(AnyUser(), "alice@example.com", "http://reset");

        // Assert
        Assert.Equal("alice@example.com", Single(sent).Recipient);
    }

    [Fact]
    public async Task SendPasswordResetLinkAsync_UsesCorrectSubject()
    {
        // Arrange
        var (sender, sent) = Build();

        // Act
        await sender.SendPasswordResetLinkAsync(AnyUser(), "alice@example.com", "http://reset");

        // Assert
        Assert.Equal("Reset your password", Single(sent).Subject);
    }

    [Fact]
    public async Task SendPasswordResetLinkAsync_BodyContainsLink()
    {
        // Arrange
        var (sender, sent) = Build();

        // Act
        await sender.SendPasswordResetLinkAsync(
            AnyUser(),
            "alice@example.com",
            "http://reset?token=xyz"
        );

        // Assert
        Assert.Contains("http://reset?token=xyz", Single(sent).Body);
    }

    #endregion

    #region SendPasswordResetCodeAsync

    [Fact]
    public async Task SendPasswordResetCodeAsync_SendsToCorrectRecipient()
    {
        // Arrange
        var (sender, sent) = Build();

        // Act
        await sender.SendPasswordResetCodeAsync(AnyUser(), "alice@example.com", "CODE123");

        // Assert
        Assert.Equal("alice@example.com", Single(sent).Recipient);
    }

    [Fact]
    public async Task SendPasswordResetCodeAsync_UsesCorrectSubject()
    {
        // Arrange
        var (sender, sent) = Build();

        // Act
        await sender.SendPasswordResetCodeAsync(AnyUser(), "alice@example.com", "CODE123");

        // Assert
        Assert.Equal("Reset your password", Single(sent).Subject);
    }

    [Fact]
    public async Task SendPasswordResetCodeAsync_BodyContainsCode()
    {
        // Arrange
        var (sender, sent) = Build();

        // Act
        await sender.SendPasswordResetCodeAsync(AnyUser(), "alice@example.com", "CODE123");

        // Assert
        Assert.Contains("CODE123", Single(sent).Body);
    }

    #endregion
}
