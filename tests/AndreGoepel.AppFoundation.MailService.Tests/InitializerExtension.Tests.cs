using Microsoft.AspNetCore.Builder;
using Microsoft.Extensions.DependencyInjection;
using NSubstitute;

namespace AndreGoepel.AppFoundation.MailService.Tests;

public class InitializerExtensionTests
{
    [Fact]
    public void AddEmailService_RegistersIEmailSender_AsSmtpEmailSender()
    {
        // Arrange
        var builder = WebApplication.CreateBuilder();
        // SmtpEmailSender depends on the settings provider, which needs Marten
        // and DataProtection — supplied by AddAppFoundation in a real host.
        builder.Services.AddSingleton(Substitute.For<Marten.IDocumentStore>());
        builder.Services.AddDataProtection();

        // Act
        builder.AddEmailService();
        using var sp = builder.Services.BuildServiceProvider();
        var sender = sp.GetRequiredService<IEmailSender>();

        // Assert
        Assert.IsType<SmtpEmailSender>(sender);
    }

    [Fact]
    public void AddEmailService_RegistersSettingsStoreAndProvider()
    {
        // Arrange
        var builder = WebApplication.CreateBuilder();

        // Act
        builder.AddEmailService();

        // Assert — registration only; resolving requires Marten's IDocumentStore.
        Assert.Contains(
            builder.Services,
            descriptor =>
                descriptor.ServiceType == typeof(IEmailSettingsStore)
                && descriptor.ImplementationType == typeof(MartenEmailSettingsStore)
        );
        Assert.Contains(
            builder.Services,
            descriptor =>
                descriptor.ServiceType == typeof(IMailSettingsProvider)
                && descriptor.ImplementationType == typeof(MailSettingsProvider)
        );
    }
}
