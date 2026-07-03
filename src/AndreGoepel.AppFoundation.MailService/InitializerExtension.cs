using Microsoft.AspNetCore.Builder;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;

namespace AndreGoepel.AppFoundation.MailService;

public static class InitializerExtension
{
    public static void AddEmailService(this WebApplicationBuilder builder)
    {
        // The EmailSender configuration section remains the bootstrap path: it
        // applies until settings are saved to the database, which then wins.
        builder
            .Services.AddOptions<MailConfiguration>()
            .Bind(builder.Configuration.GetSection("EmailSender"))
            .ValidateDataAnnotations();
        builder.Services.AddTransient<IMailSettingsProvider, MailSettingsProvider>();
        builder.Services.AddTransient<IEmailSettingsStore, MartenEmailSettingsStore>();
        builder.Services.AddTransient<IEmailSender, SmtpEmailSender>();
    }
}
