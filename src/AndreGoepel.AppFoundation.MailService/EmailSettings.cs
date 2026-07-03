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

    /// <summary>Whether a password is available (stored or from configuration).</summary>
    public bool HasPassword { get; init; }

    /// <summary>
    /// <c>true</c> when the values come from the <c>EmailSender</c> configuration
    /// section because no database record exists yet (bootstrap path).
    /// </summary>
    public bool FromConfiguration { get; init; }
}
