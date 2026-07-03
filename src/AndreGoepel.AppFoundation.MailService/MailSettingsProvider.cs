using Marten;
using Microsoft.AspNetCore.DataProtection;
using Microsoft.Extensions.Options;

namespace AndreGoepel.AppFoundation.MailService;

/// <summary>
/// Database-first settings resolution: the persisted settings record wins;
/// without one the <c>EmailSender</c> configuration section applies (bootstrap
/// path — same behaviour as before database-backed settings existed).
/// </summary>
internal sealed class MailSettingsProvider(
    IDocumentStore store,
    IOptions<MailConfiguration> fallback,
    IDataProtectionProvider dataProtectionProvider
) : IMailSettingsProvider
{
    public async Task<MailConfiguration> GetAsync(CancellationToken cancellationToken = default)
    {
        await using var session = store.QuerySession();
        var document = await session.LoadAsync<EmailSettingsDocument>(
            EmailSettingsDocument.DocumentId,
            cancellationToken
        );
        if (document is null)
        {
            return fallback.Value;
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
