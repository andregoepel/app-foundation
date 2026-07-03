using MailKit.Net.Smtp;
using MimeKit;

namespace AndreGoepel.AppFoundation.MailService;

internal class SmtpEmailSender(IMailSettingsProvider settingsProvider) : IEmailSender
{
    public async Task SendAsync(
        string recipient,
        string subject,
        string body,
        CancellationToken cancellationToken = default
    )
    {
        var configuration = await settingsProvider.GetAsync(cancellationToken);

        var message = new MimeMessage();
        message.From.Add(new MailboxAddress(configuration.SenderName, configuration.SenderEmail));
        message.To.Add(MailboxAddress.Parse(recipient));
        message.Subject = subject;
        message.Body = new TextPart(configuration.Html ? "html" : "plain") { Text = body };

        await SendMailAsync(configuration, message, cancellationToken);
    }

    protected virtual async Task SendMailAsync(
        MailConfiguration configuration,
        MimeMessage message,
        CancellationToken cancellationToken = default
    )
    {
        using var client = new SmtpClient();

        await client.ConnectAsync(
            configuration.Server,
            configuration.Port,
            configuration.UseSsl,
            cancellationToken
        );
        await client.AuthenticateAsync(
            configuration.Username,
            configuration.Password,
            cancellationToken
        );
        await client.SendAsync(message, cancellationToken);
        await client.DisconnectAsync(true, cancellationToken);
    }
}
