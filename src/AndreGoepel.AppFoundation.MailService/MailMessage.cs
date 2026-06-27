namespace AndreGoepel.AppFoundation.MailService;

public record MailMessage(string Recipient, string Subject, string Body);
