using AndreGoepel.Marten.Identity.Users;
using Microsoft.AspNetCore.Identity;

namespace AndreGoepel.Marten.Identity.Tests.Users;

public class UserPasskeyInfoExtensionTests
{
    private static readonly byte[] DefaultCredentialId = [1, 2, 3, 4];
    private static readonly byte[] DefaultPublicKey = [5, 6, 7, 8];
    private static readonly DateTimeOffset DefaultCreatedAt = new(2025, 1, 1, 0, 0, 0, TimeSpan.Zero);
    private static readonly string[] DefaultTransports = ["internal", "usb"];
    private static readonly byte[] DefaultAttestation = [10, 11];
    private static readonly byte[] DefaultClientData = [20, 21];

    private static UserPasskeyInfo Make(
        byte[]? credentialId = null,
        byte[]? publicKey = null,
        DateTimeOffset? createdAt = null,
        uint signCount = 5,
        string[]? transports = null,
        bool isUserVerified = true,
        bool isBackupEligible = false,
        bool isBackedUp = false,
        byte[]? attestationObject = null,
        byte[]? clientDataJson = null) =>
        new(
            credentialId ?? DefaultCredentialId,
            publicKey ?? DefaultPublicKey,
            createdAt ?? DefaultCreatedAt,
            signCount,
            transports ?? DefaultTransports,
            isUserVerified,
            isBackupEligible,
            isBackedUp,
            attestationObject ?? DefaultAttestation,
            clientDataJson ?? DefaultClientData
        );

    [Fact]
    public void OnlyCountChanged_IdenticalPasskeys_ReturnsTrue()
    {
        var a = Make();
        var b = Make();

        Assert.True(a.OnlyCountChanged(b));
    }

    [Fact]
    public void OnlyCountChanged_DifferentSignCountOnly_ReturnsTrue()
    {
        var a = Make(signCount: 5);
        var b = Make(signCount: 99);

        // All fields except SignCount are the same → returns true
        Assert.True(a.OnlyCountChanged(b));
    }

    [Fact]
    public void OnlyCountChanged_DifferentCredentialId_ReturnsFalse()
    {
        var a = Make();
        var b = Make(credentialId: [9, 9, 9, 9]);

        Assert.False(a.OnlyCountChanged(b));
    }

    [Fact]
    public void OnlyCountChanged_DifferentPublicKey_ReturnsFalse()
    {
        var a = Make();
        var b = Make(publicKey: [99]);

        Assert.False(a.OnlyCountChanged(b));
    }

    [Fact]
    public void OnlyCountChanged_DifferentCreatedAt_ReturnsFalse()
    {
        var a = Make();
        var b = Make(createdAt: DateTimeOffset.UtcNow.AddDays(1));

        Assert.False(a.OnlyCountChanged(b));
    }

    [Fact]
    public void OnlyCountChanged_DifferentTransports_ReturnsFalse()
    {
        var a = Make();
        var b = Make(transports: ["nfc"]);

        Assert.False(a.OnlyCountChanged(b));
    }

    [Fact]
    public void OnlyCountChanged_DifferentIsUserVerified_ReturnsFalse()
    {
        var a = Make(isUserVerified: true);
        var b = Make(isUserVerified: false);

        Assert.False(a.OnlyCountChanged(b));
    }

    [Fact]
    public void OnlyCountChanged_DifferentIsBackupEligible_ReturnsFalse()
    {
        var a = Make(isBackupEligible: false);
        var b = Make(isBackupEligible: true);

        Assert.False(a.OnlyCountChanged(b));
    }

    [Fact]
    public void OnlyCountChanged_DifferentIsBackedUp_ReturnsFalse()
    {
        var a = Make(isBackedUp: false);
        var b = Make(isBackedUp: true);

        Assert.False(a.OnlyCountChanged(b));
    }

    [Fact]
    public void OnlyCountChanged_DifferentAttestationObject_ReturnsFalse()
    {
        var a = Make();
        var b = Make(attestationObject: [99, 98]);

        Assert.False(a.OnlyCountChanged(b));
    }

    [Fact]
    public void OnlyCountChanged_DifferentClientDataJson_ReturnsFalse()
    {
        var a = Make();
        var b = Make(clientDataJson: [88, 87]);

        Assert.False(a.OnlyCountChanged(b));
    }
}
