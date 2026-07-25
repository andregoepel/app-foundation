using AndreGoepel.Marten.Configuration;
using Microsoft.AspNetCore.DataProtection;

namespace AndreGoepel.AppFoundation.MailService;

internal sealed class MartenEmailSettingsStore(
    ISettingsStore store,
    IDataProtectionProvider dataProtectionProvider
) : IEmailSettingsStore
{
    internal const string ProtectorPurpose = "AndreGoepel.AppFoundation.MailService.EmailSettings";

    public async Task<EmailSettings> LoadAsync(CancellationToken cancellationToken = default)
    {
        var document = await store.LoadAsync<EmailSettingsDocument>(cancellationToken);
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
        var existing = await store.LoadAsync<EmailSettingsDocument>(cancellationToken);

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

        await store.SaveAsync(
            new EmailSettingsDocument
            {
                SenderName = settings.SenderName,
                SenderEmail = settings.SenderEmail,
                Server = settings.Server,
                Port = settings.Port,
                UseSsl = settings.UseSsl,
                Username = settings.Username,
                ProtectedPassword = protectedPassword,
                Html = settings.Html,
            },
            cancellationToken
        );
    }
}
