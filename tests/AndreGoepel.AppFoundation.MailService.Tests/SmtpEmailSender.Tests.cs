using MimeKit;

namespace AndreGoepel.AppFoundation.MailService.Tests;

public class SmtpEmailSenderTests
{
    private static MailConfiguration Config(bool html = true) =>
        new()
        {
            SenderName = "Test Sender",
            SenderEmail = "sender@example.com",
            Server = "smtp.example.com",
            Username = "user",
            Password = "pass",
            Html = html,
        };

    private static TestableSmtpEmailSender BuildSender(MailConfiguration config) =>
        new(new FixedSettingsProvider(config));

    [Fact]
    public async Task SendAsync_SetsFromAddress()
    {
        // Arrange
        var sender = BuildSender(Config());

        // Act
        await sender.SendAsync("to@example.com", "Subject", "Body");

        // Assert
        var from = sender.CapturedMessage!.From[0] as MailboxAddress;
        Assert.NotNull(from);
        Assert.Equal("Test Sender", from.Name);
        Assert.Equal("sender@example.com", from.Address);
    }

    [Fact]
    public async Task SendAsync_SetsToAddress()
    {
        // Arrange
        var sender = BuildSender(Config());

        // Act
        await sender.SendAsync("to@example.com", "Subject", "Body");

        // Assert
        var to = sender.CapturedMessage!.To[0] as MailboxAddress;
        Assert.NotNull(to);
        Assert.Equal("to@example.com", to.Address);
    }

    [Fact]
    public async Task SendAsync_SetsSubject()
    {
        // Arrange
        var sender = BuildSender(Config());

        // Act
        await sender.SendAsync("to@example.com", "Hello World", "Body");

        // Assert
        Assert.Equal("Hello World", sender.CapturedMessage!.Subject);
    }

    [Fact]
    public async Task SendAsync_SetsBody()
    {
        // Arrange
        var sender = BuildSender(Config());

        // Act
        await sender.SendAsync("to@example.com", "Subject", "Hello <b>World</b>");

        // Assert
        var body = sender.CapturedMessage!.Body as TextPart;
        Assert.NotNull(body);
        Assert.Equal("Hello <b>World</b>", body.Text);
    }

    [Fact]
    public async Task SendAsync_Html_True_UsesHtmlSubtype()
    {
        // Arrange
        var sender = BuildSender(Config(html: true));

        // Act
        await sender.SendAsync("to@example.com", "Subject", "Body");

        // Assert
        var body = sender.CapturedMessage!.Body as TextPart;
        Assert.NotNull(body);
        Assert.Equal("html", body.ContentType.MediaSubtype);
    }

    [Fact]
    public async Task SendAsync_Html_False_UsesPlainSubtype()
    {
        // Arrange
        var sender = BuildSender(Config(html: false));

        // Act
        await sender.SendAsync("to@example.com", "Subject", "Body");

        // Assert
        var body = sender.CapturedMessage!.Body as TextPart;
        Assert.NotNull(body);
        Assert.Equal("plain", body.ContentType.MediaSubtype);
    }

    [Fact]
    public async Task SendAsync_CallsSendMailAsyncOnce()
    {
        // Arrange
        var sender = BuildSender(Config());

        // Act
        await sender.SendAsync("to@example.com", "Subject", "Body");

        // Assert
        Assert.Equal(1, sender.SendMailCallCount);
    }

    [Fact]
    public async Task SendAsync_ResolvesSettingsPerSend()
    {
        // Arrange
        var provider = new FixedSettingsProvider(Config());
        var sender = new TestableSmtpEmailSender(provider);

        // Act
        await sender.SendAsync("to@example.com", "Subject", "Body");
        await sender.SendAsync("to@example.com", "Subject", "Body");

        // Assert
        Assert.Equal(2, provider.GetCallCount);
    }

    #region Test doubles

    private sealed class FixedSettingsProvider(MailConfiguration configuration)
        : IMailSettingsProvider
    {
        public int GetCallCount { get; private set; }

        public Task<MailConfiguration> GetAsync(CancellationToken cancellationToken = default)
        {
            GetCallCount++;
            return Task.FromResult(configuration);
        }
    }

    private sealed class TestableSmtpEmailSender(IMailSettingsProvider settingsProvider)
        : SmtpEmailSender(settingsProvider)
    {
        public MimeMessage? CapturedMessage { get; private set; }
        public int SendMailCallCount { get; private set; }

        protected override Task SendMailAsync(
            MailConfiguration configuration,
            MimeMessage message,
            CancellationToken cancellationToken = default
        )
        {
            CapturedMessage = message;
            SendMailCallCount++;
            return Task.CompletedTask;
        }
    }

    #endregion
}
