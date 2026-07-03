namespace AndreGoepel.AppFoundation.MailService;

/// <summary>
/// Load/save seam for the database-backed email settings, consumed by the
/// administration UI. Sending resolves settings separately (per send), so saves
/// take effect without an application restart.
/// </summary>
public interface IEmailSettingsStore
{
    /// <summary>
    /// Returns the effective settings: the database record when present,
    /// otherwise the <c>EmailSender</c> configuration section
    /// (<see cref="EmailSettings.FromConfiguration"/> set), otherwise blank
    /// defaults. The password is never included.
    /// </summary>
    Task<EmailSettings> LoadAsync(CancellationToken cancellationToken = default);

    /// <summary>
    /// Persists the settings. <paramref name="newPassword"/> replaces the stored
    /// SMTP password; pass <c>null</c> or empty to keep the current one (falling
    /// back to the configured password on first save).
    /// </summary>
    Task SaveAsync(
        EmailSettings settings,
        string? newPassword,
        CancellationToken cancellationToken = default
    );
}
