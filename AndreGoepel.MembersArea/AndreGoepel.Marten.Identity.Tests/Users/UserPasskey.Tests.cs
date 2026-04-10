using AndreGoepel.Marten.Identity.Users;
using Microsoft.AspNetCore.Identity;

namespace AndreGoepel.Marten.Identity.Tests.Users;

public class UserPasskeyTests
{
    private static UserPasskey MakePasskey(byte[] credentialId) => new()
    {
        PasskeyInfo = new UserPasskeyInfo(
            credentialId,
            publicKey: [1],
            createdAt: DateTimeOffset.UtcNow,
            signCount: 0,
            transports: null,
            isUserVerified: false,
            isBackupEligible: false,
            isBackedUp: false,
            attestationObject: [],
            clientDataJson: []
        )
    };

    [Fact]
    public void CredentialId_IsBase64OfBytes()
    {
        var bytes = new byte[] { 1, 2, 3, 4 };
        var passkey = MakePasskey(bytes);

        Assert.Equal(Convert.ToBase64String(bytes), passkey.CredentialId);
    }

    [Fact]
    public void Equals_SameCredentialId_ReturnsTrue()
    {
        var bytes = new byte[] { 1, 2, 3, 4 };
        var a = MakePasskey(bytes);
        var b = MakePasskey(bytes);

        Assert.True(a.Equals(b));
    }

    [Fact]
    public void Equals_DifferentCredentialId_ReturnsFalse()
    {
        var a = MakePasskey([1, 2, 3, 4]);
        var b = MakePasskey([5, 6, 7, 8]);

        Assert.False(a.Equals(b));
    }

    [Fact]
    public void Equals_Null_ReturnsFalse()
    {
        var passkey = MakePasskey([1, 2, 3]);

        Assert.False(passkey.Equals(null));
    }

    [Fact]
    public void Equals_Object_SameCredentialId_ReturnsTrue()
    {
        var bytes = new byte[] { 1, 2, 3 };
        var a = MakePasskey(bytes);
        object b = MakePasskey(bytes);

        Assert.True(a.Equals(b));
    }

    [Fact]
    public void Equals_Object_NonPasskey_ReturnsFalse()
    {
        var passkey = MakePasskey([1, 2, 3]);

        Assert.False(passkey.Equals("notapasskey"));
    }

    [Fact]
    public void GetHashCode_SameCredentialId_Equal()
    {
        var bytes = new byte[] { 1, 2, 3, 4 };
        var a = MakePasskey(bytes);
        var b = MakePasskey(bytes);

        Assert.Equal(a.GetHashCode(), b.GetHashCode());
    }

    [Fact]
    public void GetHashCode_DifferentCredentialId_NotEqual()
    {
        var a = MakePasskey([1, 2, 3]);
        var b = MakePasskey([4, 5, 6]);

        Assert.NotEqual(a.GetHashCode(), b.GetHashCode());
    }

    [Fact]
    public void UsableAsHashSetKey()
    {
        var bytes = new byte[] { 1, 2, 3 };
        var set = new HashSet<UserPasskey> { MakePasskey(bytes) };

        Assert.Contains(MakePasskey(bytes), set);
        Assert.DoesNotContain(MakePasskey([9, 9, 9]), set);
    }
}
