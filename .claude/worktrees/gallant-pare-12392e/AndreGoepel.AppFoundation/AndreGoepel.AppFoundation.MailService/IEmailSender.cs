namespace AndreGoepel.AppFoundation.MailService;

public interface IEmailSender
{
    Task SendAsync(string recipient, string subject, string body);
}
