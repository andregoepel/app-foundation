using Marten;
using Microsoft.AspNetCore.DataProtection;
using Microsoft.Extensions.Options;
using NSubstitute;

namespace AndreGoepel.AppFoundation.MailService.Tests;

public class MartenEmailSettingsStoreTests
{
    private readonly IDocumentStore store = Substitute.For<IDocumentStore>();
    private readonly IDocumentSession session = Substitute.For<IDocumentSession>();
    private readonly IQuerySession querySession = Substitute.For<IQuerySession>();
    private readonly EphemeralDataProtectionProvider dataProtection = new();

    public MartenEmailSettingsStoreTests()
    {
        store.LightweightSession().Returns(session);
        store.QuerySession().Returns(querySession);
    }

    private MartenEmailSettingsStore BuildStore(MailConfiguration? configuration = null) =>
        new(
            store,
            configuration is null ? new UnconfiguredOptions() : Options.Create(configuration),
            dataProtection
        );

    /// <summary>
    /// Mimics an absent EmailSender configuration section: data-annotation
    /// validation throws on first access, exactly like the bound options do.
    /// </summary>
    private sealed class UnconfiguredOptions : IOptions<MailConfiguration>
    {
        public MailConfiguration Value =>
            throw new OptionsValidationException(
                Options.DefaultName,
                typeof(MailConfiguration),
                ["required"]
            );
    }

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

    private static MailConfiguration Configuration() =>
        new()
        {
            SenderName = "Config Sender",
            SenderEmail = "config@example.com",
            Server = "config.smtp.example.com",
            Username = "config-user",
            Password = "config-pass",
        };

    [Fact]
    public async Task LoadAsync_WithDatabaseRecord_ReturnsItWithoutPassword()
    {
        // Arrange
        querySession
            .LoadAsync<EmailSettingsDocument>(
                EmailSettingsDocument.DocumentId,
                Arg.Any<CancellationToken>()
            )
            .Returns(Document());

        // Act
        var settings = await BuildStore(Configuration()).LoadAsync();

        // Assert
        Assert.Equal("DB Sender", settings.SenderName);
        Assert.Equal(2525, settings.Port);
        Assert.True(settings.HasPassword);
        Assert.False(settings.FromConfiguration);
    }

    [Fact]
    public async Task LoadAsync_WithoutRecord_FallsBackToConfiguration()
    {
        // Act
        var settings = await BuildStore(Configuration()).LoadAsync();

        // Assert
        Assert.Equal("Config Sender", settings.SenderName);
        Assert.True(settings.HasPassword);
        Assert.True(settings.FromConfiguration);
    }

    [Fact]
    public async Task LoadAsync_WithoutRecordAndConfiguration_ReturnsBlankDefaults()
    {
        // Act
        var settings = await BuildStore().LoadAsync();

        // Assert
        Assert.Equal("", settings.SenderName);
        Assert.Equal(587, settings.Port);
        Assert.False(settings.HasPassword);
    }

    [Fact]
    public async Task SaveAsync_WithNewPassword_StoresProtectedPassword()
    {
        // Arrange
        var emailStore = BuildStore(Configuration());
        EmailSettingsDocument? stored = null;
        session.Store(Arg.Do<EmailSettingsDocument[]>(documents => stored = documents.Single()));

        // Act
        await emailStore.SaveAsync(
            new EmailSettings
            {
                SenderName = "S",
                SenderEmail = "s@example.com",
                Server = "smtp",
                Username = "u",
            },
            "new-secret"
        );

        // Assert
        Assert.NotNull(stored);
        Assert.NotEqual("new-secret", stored.ProtectedPassword);
        Assert.Equal(
            "new-secret",
            dataProtection
                .CreateProtector(MartenEmailSettingsStore.ProtectorPurpose)
                .Unprotect(stored.ProtectedPassword)
        );
        await session.Received(1).SaveChangesAsync(Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task SaveAsync_WithoutPassword_KeepsExistingProtectedPassword()
    {
        // Arrange
        var existing = Document();
        session
            .LoadAsync<EmailSettingsDocument>(
                EmailSettingsDocument.DocumentId,
                Arg.Any<CancellationToken>()
            )
            .Returns(existing);
        EmailSettingsDocument? stored = null;
        session.Store(Arg.Do<EmailSettingsDocument[]>(documents => stored = documents.Single()));

        // Act
        await BuildStore(Configuration())
            .SaveAsync(
                new EmailSettings
                {
                    SenderName = "S",
                    SenderEmail = "s@example.com",
                    Server = "smtp",
                    Username = "u",
                },
                newPassword: null
            );

        // Assert
        Assert.NotNull(stored);
        Assert.Equal("protected", stored.ProtectedPassword);
    }

    [Fact]
    public async Task SaveAsync_FirstSaveWithoutPassword_FallsBackToConfiguredPassword()
    {
        // Arrange
        var emailStore = BuildStore(Configuration());
        EmailSettingsDocument? stored = null;
        session.Store(Arg.Do<EmailSettingsDocument[]>(documents => stored = documents.Single()));

        // Act
        await emailStore.SaveAsync(
            new EmailSettings
            {
                SenderName = "S",
                SenderEmail = "s@example.com",
                Server = "smtp",
                Username = "u",
            },
            newPassword: null
        );

        // Assert
        Assert.NotNull(stored);
        Assert.Equal(
            "config-pass",
            dataProtection
                .CreateProtector(MartenEmailSettingsStore.ProtectorPurpose)
                .Unprotect(stored.ProtectedPassword)
        );
    }

    [Fact]
    public async Task SaveAsync_FirstSaveWithoutAnyPassword_Throws()
    {
        // Act + Assert
        await Assert.ThrowsAsync<InvalidOperationException>(() =>
            BuildStore()
                .SaveAsync(
                    new EmailSettings
                    {
                        SenderName = "S",
                        SenderEmail = "s@example.com",
                        Server = "smtp",
                        Username = "u",
                    },
                    newPassword: null
                )
        );
    }
}
