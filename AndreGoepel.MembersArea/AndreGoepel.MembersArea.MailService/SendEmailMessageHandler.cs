using Wolverine.Attributes;

namespace AndreGoepel.MembersArea.MailService;

[WolverineHandler]
public class SendEmailMessageHandler(IEmailSender EmailSender)
{
    public async Task Handle(MailMessage message)
    {
        await EmailSender.SendAsync(message.Recipient, message.Subject, message.Body);
    }
}
