using MailKit.Net.Smtp;
using Microsoft.Extensions.Options;
using MimeKit;

namespace AndreGoepel.MembersArea.MailService;

internal class SmtpEmailSender(IOptions<MailConfiguration> configuration) : IEmailSender
{
    public async Task SendAsync(string recipient, string subject, string body)
    {
        var message = new MimeMessage();
        message.From.Add(
            new MailboxAddress(configuration.Value.SenderName, configuration.Value.SenderEmail)
        );
        message.To.Add(MailboxAddress.Parse(recipient));
        message.Subject = subject;
        message.Body = new TextPart(configuration.Value.Html ? "html" : "plain") { Text = body };

        await SendMailAsync(message);
    }

    private async Task SendMailAsync(MimeMessage message)
    {
        using var client = new SmtpClient();

        await client.ConnectAsync(
            configuration.Value.Server,
            configuration.Value.Port,
            configuration.Value.UseSsl
        );
        await client.AuthenticateAsync(configuration.Value.Username, configuration.Value.Password);
        await client.SendAsync(message);
        await client.DisconnectAsync(true);
    }
}
