using AndreGoepel.Marten.Configuration;
using Microsoft.AspNetCore.DataProtection;
using NSubstitute;

namespace AndreGoepel.AppFoundation.MailService.Tests;

public sealed class MailSettingsProviderTests
{
    private readonly ISettingsStore store = Substitute.For<ISettingsStore>();
    private readonly EphemeralDataProtectionProvider dataProtection = new();

    [Fact]
    public async Task GetAsync_WithDatabaseRecord_ReturnsItWithUnprotectedPassword()
    {
        // Arrange
        var protector = dataProtection.CreateProtector(MartenEmailSettingsStore.ProtectorPurpose);
        store
            .LoadAsync<EmailSettingsDocument>(Arg.Any<CancellationToken>())
            .Returns(
                new EmailSettingsDocument
                {
                    SenderName = "DB Sender",
                    SenderEmail = "db@example.com",
                    Server = "db.smtp.example.com",
                    Port = 2525,
                    UseSsl = true,
                    Username = "db-user",
                    ProtectedPassword = protector.Protect("db-secret"),
                    Html = false,
                }
            );
        var provider = new MailSettingsProvider(store, dataProtection);

        // Act
        var configuration = await provider.GetAsync(TestContext.Current.CancellationToken);

        // Assert
        Assert.Equal("DB Sender", configuration.SenderName);
        Assert.Equal(2525, configuration.Port);
        Assert.Equal("db-secret", configuration.Password);
        Assert.False(configuration.Html);
    }

    [Fact]
    public async Task GetAsync_WithoutRecord_ReturnsBlankDefaults()
    {
        // Arrange
        var provider = new MailSettingsProvider(store, dataProtection);

        // Act
        var configuration = await provider.GetAsync(TestContext.Current.CancellationToken);

        // Assert
        Assert.Equal("", configuration.SenderName);
        Assert.Equal("", configuration.Server);
        Assert.Equal(587, configuration.Port);
    }
}
