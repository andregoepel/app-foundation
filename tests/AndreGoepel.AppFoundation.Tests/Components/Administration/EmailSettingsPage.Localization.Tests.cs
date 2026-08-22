using AndreGoepel.AppFoundation.Components.Administration.Pages;
using AndreGoepel.AppFoundation.MailService;
using Bunit;
using Microsoft.Extensions.DependencyInjection;
using NSubstitute;
using Radzen;

namespace AndreGoepel.AppFoundation.Tests.Components.Administration;

public sealed class EmailSettingsPageLocalizationTests : BunitContext
{
    private readonly IEmailSettingsStore store = Substitute.For<IEmailSettingsStore>();
    private readonly IEmailSender emailSender = Substitute.For<IEmailSender>();

    public EmailSettingsPageLocalizationTests()
    {
        this.UseLooseJSInterop();
        Services.AddSingleton(store);
        Services.AddSingleton(emailSender);
        Services.AddSingleton(new NotificationService());
        store
            .LoadAsync(Arg.Any<CancellationToken>())
            .Returns(
                new EmailSettings
                {
                    SenderName = "Acme Mailer",
                    SenderEmail = "mail@acme.example",
                    Server = "smtp.acme.example",
                    Port = 2525,
                    Username = "acme-user",
                    HasPassword = true,
                }
            );
    }

    [Fact]
    public void Render_German_ShowsGermanCopy()
    {
        // Arrange
        using var culture = CultureScope.UiOnly("de");

        // Act
        var cut = Render<EmailSettingsPage>();

        // Assert
        Assert.Contains("E-Mail-Einstellungen", cut.Markup);
        Assert.Contains("Absendername", cut.Markup);
        Assert.Contains("Änderungen speichern", cut.Markup);
    }
}
