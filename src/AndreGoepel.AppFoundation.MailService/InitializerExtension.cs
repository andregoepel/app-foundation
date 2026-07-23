using Microsoft.AspNetCore.Builder;
using Microsoft.Extensions.DependencyInjection;

namespace AndreGoepel.AppFoundation.MailService;

public static class InitializerExtension
{
    public static void AddEmailService(this WebApplicationBuilder builder)
    {
        builder.Services.AddTransient<IMailSettingsProvider, MailSettingsProvider>();
        builder.Services.AddTransient<IEmailSettingsStore, MartenEmailSettingsStore>();
        builder.Services.AddTransient<IEmailSender, SmtpEmailSender>();
    }
}
