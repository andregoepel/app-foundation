using AndreGoepel.AppFoundation.Core;
using AndreGoepel.Marten.Configuration;
using Microsoft.AspNetCore.DataProtection;
using NSubstitute;

namespace AndreGoepel.AppFoundation.MailService.Tests;

public sealed class MartenEmailSettingsStoreTests
{
    private readonly ISettingsStore store = Substitute.For<ISettingsStore>();
    private readonly EphemeralDataProtectionProvider dataProtection = new();

    private MartenEmailSettingsStore BuildStore() => new(store, dataProtection);

    private static EmailSettingsDocument Document() =>
        new()
        {
            SenderName = "DB Sender",
            SenderEmail = "db@example.com",
            Server = "db.smtp.example.com",
            Port = 2525,
            UseSsl = true,
            Username = "db-user",
            ProtectedPassword = "protected",
            Html = false,
        };

    [Fact]
    public async Task LoadAsync_WithDatabaseRecord_ReturnsItWithoutPassword()
    {
        // Arrange
        store.LoadAsync<EmailSettingsDocument>(Arg.Any<CancellationToken>()).Returns(Document());

        // Act
        var settings = await BuildStore().LoadAsync(TestContext.Current.CancellationToken);

        // Assert
        Assert.Equal("DB Sender", settings.SenderName);
        Assert.Equal(2525, settings.Port);
        Assert.True(settings.HasPassword);
    }

    [Fact]
    public async Task LoadAsync_WithoutRecord_ReturnsBlankDefaults()
    {
        // Act
        var settings = await BuildStore().LoadAsync(TestContext.Current.CancellationToken);

        // Assert
        Assert.Equal("", settings.SenderName);
        Assert.Equal(587, settings.Port);
        Assert.False(settings.HasPassword);
    }

    [Fact]
    public async Task SaveAsync_WithNewPassword_StoresProtectedPassword()
    {
        // Arrange
        var emailStore = BuildStore();
        EmailSettingsDocument? stored = null;
        // Substitute configuration call, not a real invocation — discard is intentional.
        _ = store.SaveAsync(
            Arg.Do<EmailSettingsDocument>(document => stored = document),
            Arg.Any<CancellationToken>()
        );

        // Act
        var result = await emailStore.SaveAsync(
            new EmailSettings
            {
                SenderName = "S",
                SenderEmail = "s@example.com",
                Server = "smtp",
                Username = "u",
            },
            "new-secret",
            TestContext.Current.CancellationToken
        );

        // Assert
        Assert.True(result.IsSuccess);
        Assert.NotNull(stored);
        Assert.NotEqual("new-secret", stored.ProtectedPassword);
        Assert.Equal(
            "new-secret",
            dataProtection
                .CreateProtector(MartenEmailSettingsStore.ProtectorPurpose)
                .Unprotect(stored.ProtectedPassword)
        );
        await store
            .Received(1)
            .SaveAsync(Arg.Any<EmailSettingsDocument>(), Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task SaveAsync_WithoutPassword_KeepsExistingProtectedPassword()
    {
        // Arrange
        var existing = Document();
        store.LoadAsync<EmailSettingsDocument>(Arg.Any<CancellationToken>()).Returns(existing);
        EmailSettingsDocument? stored = null;
        // Substitute configuration call, not a real invocation — discard is intentional.
        _ = store.SaveAsync(
            Arg.Do<EmailSettingsDocument>(document => stored = document),
            Arg.Any<CancellationToken>()
        );

        // Act
        var result = await BuildStore()
            .SaveAsync(
                new EmailSettings
                {
                    SenderName = "S",
                    SenderEmail = "s@example.com",
                    Server = "smtp",
                    Username = "u",
                },
                newPassword: null,
                TestContext.Current.CancellationToken
            );

        // Assert
        Assert.True(result.IsSuccess);
        Assert.NotNull(stored);
        Assert.Equal("protected", stored.ProtectedPassword);
    }

    [Fact]
    public async Task SaveAsync_FirstSaveWithoutAnyPassword_ReturnsFailure()
    {
        // Act
        var result = await BuildStore()
            .SaveAsync(
                new EmailSettings
                {
                    SenderName = "S",
                    SenderEmail = "s@example.com",
                    Server = "smtp",
                    Username = "u",
                },
                newPassword: null,
                TestContext.Current.CancellationToken
            );

        // Assert
        Assert.True(result.IsFailure);
        Assert.Equal("An SMTP password is required for the first save.", result.Error);
    }
}
