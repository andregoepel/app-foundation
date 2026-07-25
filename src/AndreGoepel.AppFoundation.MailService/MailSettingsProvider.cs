using AndreGoepel.Marten.Configuration;
using Microsoft.AspNetCore.DataProtection;

namespace AndreGoepel.AppFoundation.MailService;

/// <summary>
/// Database-only settings resolution: without a persisted record, sending fails loudly rather
/// than silently — there is no configuration fallback, so the app starts with nothing configured
/// until an administrator saves it on the Email settings page.
/// </summary>
// Public for the same Wolverine codegen reason as SmtpEmailSender: it is constructed
// inside the generated MailMessage handler as SmtpEmailSender's dependency.
public sealed class MailSettingsProvider(
    ISettingsStore store,
    IDataProtectionProvider dataProtectionProvider
) : IMailSettingsProvider
{
    public async Task<MailConfiguration> GetAsync(CancellationToken cancellationToken = default)
    {
        var document = await store.LoadAsync<EmailSettingsDocument>(cancellationToken);
        if (document is null)
        {
            return new MailConfiguration();
        }

        var protector = dataProtectionProvider.CreateProtector(
            MartenEmailSettingsStore.ProtectorPurpose
        );
        return new MailConfiguration
        {
            SenderName = document.SenderName,
            SenderEmail = document.SenderEmail,
            Server = document.Server,
            Port = document.Port,
            UseSsl = document.UseSsl,
            Username = document.Username,
            Password = protector.Unprotect(document.ProtectedPassword),
            Html = document.Html,
        };
    }
}
