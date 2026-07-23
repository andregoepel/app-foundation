using Marten;
using Microsoft.AspNetCore.DataProtection;
using Microsoft.Extensions.Options;

namespace AndreGoepel.AppFoundation.MailService;

internal sealed class MartenEmailSettingsStore(
    IDocumentStore store,
    IOptions<MailConfiguration> fallback,
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
        if (document is not null)
        {
            return new EmailSettings
            {
                SenderName = document.SenderName,
                SenderEmail = document.SenderEmail,
                Server = document.Server,
                Port = document.Port,
                UseSsl = document.UseSsl,
                Username = document.Username,
                Html = document.Html,
                HasPassword = true,
                FromConfiguration = false,
            };
        }

        var configured = TryGetConfiguration();
        if (configured is null)
        {
            return new EmailSettings();
        }

        return new EmailSettings
        {
            SenderName = configured.SenderName,
            SenderEmail = configured.SenderEmail,
            Server = configured.Server,
            Port = configured.Port,
            UseSsl = configured.UseSsl,
            Username = configured.Username,
            Html = configured.Html,
            HasPassword = !string.IsNullOrEmpty(configured.Password),
            FromConfiguration = true,
        };
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
        else if (TryGetConfiguration()?.Password is { Length: > 0 } configuredPassword)
        {
            protectedPassword = protector.Protect(configuredPassword);
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

    private MailConfiguration? TryGetConfiguration()
    {
        try
        {
            return fallback.Value;
        }
        catch (OptionsValidationException)
        {
            // No (or incomplete) EmailSender configuration section — valid when
            // settings are managed in the database.
            return null;
        }
    }
}
