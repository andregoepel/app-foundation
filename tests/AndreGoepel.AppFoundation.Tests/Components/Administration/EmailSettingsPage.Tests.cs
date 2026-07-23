using AndreGoepel.AppFoundation.Components.Administration.Pages;
using AndreGoepel.AppFoundation.MailService;
using Bunit;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Components;
using Microsoft.Extensions.DependencyInjection;
using NSubstitute;
using Radzen;

namespace AndreGoepel.AppFoundation.Tests.Components.Administration;

public class EmailSettingsPageTests : BunitContext
{
    private readonly IEmailSettingsStore store = Substitute.For<IEmailSettingsStore>();
    private readonly IEmailSender emailSender = Substitute.For<IEmailSender>();

    public EmailSettingsPageTests()
    {
        JSInterop.Mode = JSRuntimeMode.Loose;
        Services.AddSingleton(store);
        Services.AddSingleton(emailSender);
        Services.AddSingleton(new NotificationService());
    }

    private static EmailSettings Settings() =>
        new()
        {
            SenderName = "Acme Mailer",
            SenderEmail = "mail@acme.example",
            Server = "smtp.acme.example",
            Port = 2525,
            UseSsl = true,
            Username = "acme-user",
            Html = true,
            HasPassword = true,
        };

    [Fact]
    public void Render_LoadsSettingsIntoForm()
    {
        // Arrange
        store.LoadAsync(Arg.Any<CancellationToken>()).Returns(Settings());

        // Act
        var cut = Render<EmailSettingsPage>();

        // Assert
        Assert.Contains("Acme Mailer", cut.Markup);
        Assert.Contains("smtp.acme.example", cut.Markup);
        Assert.Contains("acme-user", cut.Markup);
    }

    [Fact]
    public void Submit_WithValidInput_SavesWithoutPasswordChange()
    {
        // Arrange
        store.LoadAsync(Arg.Any<CancellationToken>()).Returns(Settings());
        var cut = Render<EmailSettingsPage>();

        // Act
        cut.Find("form").Submit();

        // Assert
        cut.WaitForAssertion(() =>
            store
                .Received(1)
                .SaveAsync(
                    Arg.Is<EmailSettings>(settings =>
                        settings.SenderName == "Acme Mailer" && settings.Port == 2525
                    ),
                    null,
                    Arg.Any<CancellationToken>()
                )
        );
    }

    [Fact]
    public void Route_IsAdministrationEmailSettings_AndRequiresAdministratorRole()
    {
        // Act
        var route = Attribute.GetCustomAttribute(typeof(EmailSettingsPage), typeof(RouteAttribute));
        var authorize =
            Attribute.GetCustomAttribute(typeof(EmailSettingsPage), typeof(AuthorizeAttribute))
            as AuthorizeAttribute;

        // Assert
        Assert.Equal(
            "/Administration/EmailSettings",
            Assert.IsType<RouteAttribute>(route).Template
        );
        Assert.NotNull(authorize);
        Assert.Equal("Administrator", authorize.Roles);
    }
}
