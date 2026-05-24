using AndreGoepel.Marten.Identity.Users;
using Microsoft.AspNetCore.Identity;

namespace AndreGoepel.Marten.Identity.Tests.Users;

public class UserPasskeyTests
{
    private static UserPasskey MakePasskey(byte[] credentialId) =>
        new()
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
            ),
        };

    [Fact]
    public void CredentialId_IsBase64OfBytes()
    {
        // Arrange
        var bytes = new byte[] { 1, 2, 3, 4 };
        var passkey = MakePasskey(bytes);

        // Act
        var result = passkey.CredentialId;

        // Assert
        Assert.Equal(Convert.ToBase64String(bytes), result);
    }

    [Fact]
    public void Equals_SameCredentialId_ReturnsTrue()
    {
        // Arrange
        var bytes = new byte[] { 1, 2, 3, 4 };
        var a = MakePasskey(bytes);
        var b = MakePasskey(bytes);

        // Act
        var result = a.Equals(b);

        // Assert
        Assert.True(result);
    }

    [Fact]
    public void Equals_DifferentCredentialId_ReturnsFalse()
    {
        // Arrange
        var a = MakePasskey([1, 2, 3, 4]);
        var b = MakePasskey([5, 6, 7, 8]);

        // Act
        var result = a.Equals(b);

        // Assert
        Assert.False(result);
    }

    [Fact]
    public void Equals_Null_ReturnsFalse()
    {
        // Arrange
        var passkey = MakePasskey([1, 2, 3]);

        // Act
        var result = passkey.Equals(null);

        // Assert
        Assert.False(result);
    }

    [Fact]
    public void Equals_Object_SameCredentialId_ReturnsTrue()
    {
        // Arrange
        var bytes = new byte[] { 1, 2, 3 };
        var a = MakePasskey(bytes);
        object b = MakePasskey(bytes);

        // Act
        var result = a.Equals(b);

        // Assert
        Assert.True(result);
    }

    [Fact]
    public void Equals_Object_NonPasskey_ReturnsFalse()
    {
        // Arrange
        var passkey = MakePasskey([1, 2, 3]);

        // Act
        var result = passkey.Equals("notapasskey");

        // Assert
        Assert.False(result);
    }

    [Fact]
    public void GetHashCode_SameCredentialId_Equal()
    {
        // Arrange
        var bytes = new byte[] { 1, 2, 3, 4 };
        var a = MakePasskey(bytes);
        var b = MakePasskey(bytes);

        // Act
        var hashA = a.GetHashCode();
        var hashB = b.GetHashCode();

        // Assert
        Assert.Equal(hashA, hashB);
    }

    [Fact]
    public void GetHashCode_DifferentCredentialId_NotEqual()
    {
        // Arrange
        var a = MakePasskey([1, 2, 3]);
        var b = MakePasskey([4, 5, 6]);

        // Act
        var hashA = a.GetHashCode();
        var hashB = b.GetHashCode();

        // Assert
        Assert.NotEqual(hashA, hashB);
    }

    [Fact]
    public void UsableAsHashSetKey()
    {
        // Arrange
        var bytes = new byte[] { 1, 2, 3 };
        var set = new HashSet<UserPasskey> { MakePasskey(bytes) };

        // Act
        var containsMatch = set.Contains(MakePasskey(bytes));
        var containsDifferent = set.Contains(MakePasskey([9, 9, 9]));

        // Assert
        Assert.True(containsMatch);
        Assert.False(containsDifferent);
    }
}
