using Microsoft.AspNetCore.Builder;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;

namespace AndreGoepel.AppFoundation.MailService;

public static class InitializerExtension
{
    public static void AddEmailService(this WebApplicationBuilder builder)
    {
        builder
            .Services.AddOptions<MailConfiguration>()
            .Bind(builder.Configuration.GetSection("EmailSender"))
            .ValidateDataAnnotations();
        builder.Services.AddTransient<IEmailSender, SmtpEmailSender>();
    }
}
