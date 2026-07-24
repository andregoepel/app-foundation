using Marten;
using Microsoft.AspNetCore.DataProtection;

namespace AndreGoepel.AppFoundation.MailService;

internal sealed class MartenEmailSettingsStore(
    IDocumentStore store,
    IDataProtectionProvider dataProtectionProvider
) : IEmailSettingsStore
{
    internal const string ProtectorPurpose = "AndreGoepel.AppFoundation.MailService.EmailSettings";

    public async Task<EmailSettings> LoadAsync(CancellationToken cancellationToken = default)
    {
        await using var session = store.QuerySession();
        var document = await session.LoadAsync<EmailSettingsDocument>(
            EmailSettingsDocument.DocumentId,
            cancellationToken
        );
        return document is not null
            ? new EmailSettings
            {
                SenderName = document.SenderName,
                SenderEmail = document.SenderEmail,
                Server = document.Server,
                Port = document.Port,
                UseSsl = document.UseSsl,
                Username = document.Username,
                Html = document.Html,
                HasPassword = true,
            }
            : new EmailSettings();
    }

    public async Task SaveAsync(
        EmailSettings settings,
        string? newPassword,
        CancellationToken cancellationToken = default
    )
    {
        await using var session = store.LightweightSession();
        var existing = await session.LoadAsync<EmailSettingsDocument>(
            EmailSettingsDocument.DocumentId,
            cancellationToken
        );

        var protector = dataProtectionProvider.CreateProtector(ProtectorPurpose);
        string protectedPassword;
        if (!string.IsNullOrEmpty(newPassword))
        {
            protectedPassword = protector.Protect(newPassword);
        }
        else if (existing is not null)
        {
            protectedPassword = existing.ProtectedPassword;
        }
        else
        {
            throw new InvalidOperationException("An SMTP password is required for the first save.");
        }

        session.Store(
            new EmailSettingsDocument
            {
                Id = EmailSettingsDocument.DocumentId,
                SenderName = settings.SenderName,
                SenderEmail = settings.SenderEmail,
                Server = settings.Server,
                Port = settings.Port,
                UseSsl = settings.UseSsl,
                Username = settings.Username,
                ProtectedPassword = protectedPassword,
                Html = settings.Html,
            }
        );
        await session.SaveChangesAsync(cancellationToken);
    }
}
