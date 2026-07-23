namespace AndreGoepel.AppFoundation.MailService;

/// <summary>
/// Email settings as exposed to the administration UI. The SMTP password never
/// round-trips: <see cref="HasPassword"/> only signals that one is stored.
/// </summary>
public sealed record EmailSettings
{
    public string SenderName { get; init; } = "";

    public string SenderEmail { get; init; } = "";

    public string Server { get; init; } = "";

    public int Port { get; init; } = 587;

    public bool UseSsl { get; init; }

    public string Username { get; init; } = "";

    public bool Html { get; init; } = true;

    /// <summary>Whether a password is stored.</summary>
    public bool HasPassword { get; init; }
}
