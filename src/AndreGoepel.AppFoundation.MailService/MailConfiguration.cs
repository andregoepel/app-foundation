namespace AndreGoepel.AppFoundation.MailService;

public record MailConfiguration
{
    public string SenderName { get; init; } = "";

    public string SenderEmail { get; init; } = "";

    public string Server { get; init; } = "";

    public int Port { get; init; } = 587;

    public bool UseSsl { get; init; }

    public string Username { get; init; } = "";

    public string Password { get; init; } = "";

    public bool Html { get; init; } = true;
}
