using Marten;
using Microsoft.AspNetCore.DataProtection;
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

    private MartenEmailSettingsStore BuildStore() => new(store, dataProtection);

    private static EmailSettingsDocument Document() =>
        new()
        {
            Id = EmailSettingsDocument.DocumentId,
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
        querySession
            .LoadAsync<EmailSettingsDocument>(
                EmailSettingsDocument.DocumentId,
                Arg.Any<CancellationToken>()
            )
            .Returns(Document());

        // Act
        var settings = await BuildStore().LoadAsync();

        // Assert
        Assert.Equal("DB Sender", settings.SenderName);
        Assert.Equal(2525, settings.Port);
        Assert.True(settings.HasPassword);
    }

    [Fact]
    public async Task LoadAsync_WithoutRecord_ReturnsBlankDefaults()
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
        var emailStore = BuildStore();
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
        await BuildStore()
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
