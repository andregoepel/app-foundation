namespace AndreGoepel.AppFoundation.MailService;

/// <summary>
/// Resolves the effective <see cref="MailConfiguration"/> for sending. Looked up
/// per send so database changes take effect without an application restart.
/// </summary>
public interface IMailSettingsProvider
{
    Task<MailConfiguration> GetAsync(CancellationToken cancellationToken = default);
}
