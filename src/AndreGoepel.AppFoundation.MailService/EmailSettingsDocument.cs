namespace AndreGoepel.AppFoundation.MailService;

/// <summary>
/// Marten document holding the single email settings record — a <see cref="SettingsDocument"/>
/// subclass, so it shares a table with any other admin-configured settings record a consuming app
/// registers the same way (see <c>AndreGoepel.AppFoundation.Hosting</c>'s <c>AddAppFoundation</c>).
/// The SMTP password is stored DataProtection-protected (<see cref="ProtectedPassword"/>), never
/// in plain text.
/// </summary>
public sealed class EmailSettingsDocument : SettingsDocument
{
    public const string DocumentId = "email-settings";

    public required string SenderName { get; init; }

    public required string SenderEmail { get; init; }

    public required string Server { get; init; }

    public int Port { get; init; } = 587;

    public bool UseSsl { get; init; }

    public required string Username { get; init; }

    public required string ProtectedPassword { get; init; }

    public bool Html { get; init; } = true;
}
