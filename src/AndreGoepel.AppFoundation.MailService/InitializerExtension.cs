using Microsoft.AspNetCore.Builder;
using Microsoft.Extensions.DependencyInjection;

namespace AndreGoepel.AppFoundation.MailService;

public static class InitializerExtension
{
    public static void AddEmailService(this WebApplicationBuilder builder)
    {
        // Registers ISettingsStore so this method also works when called without
        // AddAppFoundation (e.g. in tests): TryAdd, so a host that already called it
        // wins.
        builder.Services.AddMartenConfiguration();
        builder.Services.AddTransient<IMailSettingsProvider, MailSettingsProvider>();
        builder.Services.AddTransient<IEmailSettingsStore, MartenEmailSettingsStore>();
        builder.Services.AddTransient<IEmailSender, SmtpEmailSender>();
    }
}
