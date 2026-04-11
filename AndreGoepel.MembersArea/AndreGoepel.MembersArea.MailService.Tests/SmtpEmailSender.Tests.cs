using Microsoft.Extensions.Options;
using MimeKit;

namespace AndreGoepel.MembersArea.MailService.Tests;

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
        new(Options.Create(config));

    [Fact]
    public async Task SendAsync_SetsFromAddress()
    {
        var sender = BuildSender(Config());

        await sender.SendAsync("to@example.com", "Subject", "Body");

        var from = sender.CapturedMessage!.From[0] as MailboxAddress;
        Assert.NotNull(from);
        Assert.Equal("Test Sender", from.Name);
        Assert.Equal("sender@example.com", from.Address);
    }

    [Fact]
    public async Task SendAsync_SetsToAddress()
    {
        var sender = BuildSender(Config());

        await sender.SendAsync("to@example.com", "Subject", "Body");

        var to = sender.CapturedMessage!.To[0] as MailboxAddress;
        Assert.NotNull(to);
        Assert.Equal("to@example.com", to.Address);
    }

    [Fact]
    public async Task SendAsync_SetsSubject()
    {
        var sender = BuildSender(Config());

        await sender.SendAsync("to@example.com", "Hello World", "Body");

        Assert.Equal("Hello World", sender.CapturedMessage!.Subject);
    }

    [Fact]
    public async Task SendAsync_SetsBody()
    {
        var sender = BuildSender(Config());

        await sender.SendAsync("to@example.com", "Subject", "Hello <b>World</b>");

        var body = sender.CapturedMessage!.Body as TextPart;
        Assert.NotNull(body);
        Assert.Equal("Hello <b>World</b>", body.Text);
    }

    [Fact]
    public async Task SendAsync_Html_True_UsesHtmlSubtype()
    {
        var sender = BuildSender(Config(html: true));

        await sender.SendAsync("to@example.com", "Subject", "Body");

        var body = sender.CapturedMessage!.Body as TextPart;
        Assert.NotNull(body);
        Assert.Equal("html", body.ContentType.MediaSubtype);
    }

    [Fact]
    public async Task SendAsync_Html_False_UsesPlainSubtype()
    {
        var sender = BuildSender(Config(html: false));

        await sender.SendAsync("to@example.com", "Subject", "Body");

        var body = sender.CapturedMessage!.Body as TextPart;
        Assert.NotNull(body);
        Assert.Equal("plain", body.ContentType.MediaSubtype);
    }

    [Fact]
    public async Task SendAsync_CallsSendMailAsyncOnce()
    {
        var sender = BuildSender(Config());

        await sender.SendAsync("to@example.com", "Subject", "Body");

        Assert.Equal(1, sender.SendMailCallCount);
    }

    #region Test double

    private sealed class TestableSmtpEmailSender(IOptions<MailConfiguration> options)
        : SmtpEmailSender(options)
    {
        public MimeMessage? CapturedMessage { get; private set; }
        public int SendMailCallCount { get; private set; }

        protected override Task SendMailAsync(MimeMessage message)
        {
            CapturedMessage = message;
            SendMailCallCount++;
            return Task.CompletedTask;
        }
    }

    #endregion
}
