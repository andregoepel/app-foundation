using Microsoft.AspNetCore.Builder;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;
using NSubstitute;

namespace AndreGoepel.AppFoundation.MailService.Tests;

public class InitializerExtensionTests
{
    [Fact]
    public void AddEmailService_RegistersIEmailSender_AsSmtpEmailSender()
    {
        // Arrange
        var builder = WebApplication.CreateBuilder();
        builder.Configuration.AddInMemoryCollection(ValidConfig());
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
    public void AddEmailService_BindsMailConfigurationFromEmailSenderSection()
    {
        // Arrange
        var builder = WebApplication.CreateBuilder();
        builder.Configuration.AddInMemoryCollection(ValidConfig());

        // Act
        builder.AddEmailService();
        using var sp = builder.Services.BuildServiceProvider();
        var options = sp.GetRequiredService<IOptions<MailConfiguration>>().Value;

        // Assert
        Assert.Equal("Sender", options.SenderName);
        Assert.Equal("from@example.com", options.SenderEmail);
        Assert.Equal("smtp.example.com", options.Server);
        Assert.Equal("user", options.Username);
        Assert.Equal("pw", options.Password);
    }

    [Fact]
    public void AddEmailService_MissingRequiredFields_FailsDataAnnotationValidation()
    {
        // Arrange
        var builder = WebApplication.CreateBuilder();
        // No EmailSender section configured.

        // Act
        builder.AddEmailService();
        using var sp = builder.Services.BuildServiceProvider();

        // Assert
        Assert.Throws<OptionsValidationException>(() =>
            sp.GetRequiredService<IOptions<MailConfiguration>>().Value
        );
    }

    [Fact]
    public void AddEmailService_RegistersSettingsStoreAndProvider()
    {
        // Arrange
        var builder = WebApplication.CreateBuilder();
        builder.Configuration.AddInMemoryCollection(ValidConfig());

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

    private static IEnumerable<KeyValuePair<string, string?>> ValidConfig() =>
        new Dictionary<string, string?>
        {
            ["EmailSender:SenderName"] = "Sender",
            ["EmailSender:SenderEmail"] = "from@example.com",
            ["EmailSender:Server"] = "smtp.example.com",
            ["EmailSender:Username"] = "user",
            ["EmailSender:Password"] = "pw",
        };
}
