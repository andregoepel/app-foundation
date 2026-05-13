using AndreGoepel.Marten.Identity.Users;

namespace AndreGoepel.Marten.Identity.Tests.Users;

public class UserExtensionTests
{
    private static User BaseUser() =>
        new()
        {
            Email = "alice@example.com",
            UserName = "alice@example.com",
            PasswordHash = "hash",
            EmailConfirmed = true,
            PhoneNumber = "1234567890",
            AuthenticatorKey = "authkey",
            RecoveryCodes = "code1;code2",
            TwoFactorEnabled = false,
        };

    [Fact]
    public void AreEqual_IdenticalUsers_ReturnsTrue()
    {
        // Arrange
        var a = BaseUser();
        var b = BaseUser();

        // Assert
        Assert.True(a.AreEqual(b));
    }

    [Fact]
    public void AreEqual_SameInstance_ReturnsTrue()
    {
        // Arrange
        var user = BaseUser();

        // Assert
        Assert.True(user.AreEqual(user));
    }

    [Fact]
    public void AreEqual_DifferentEmail_ReturnsFalse()
    {
        // Arrange
        var a = BaseUser();
        var b = BaseUser();
        b.Email = "bob@example.com";

        // Assert
        Assert.False(a.AreEqual(b));
    }

    [Fact]
    public void AreEqual_DifferentUserName_ReturnsFalse()
    {
        // Arrange
        var a = BaseUser();
        var b = BaseUser();
        b.UserName = "bob";

        // Assert
        Assert.False(a.AreEqual(b));
    }

    [Fact]
    public void AreEqual_DifferentPasswordHash_ReturnsFalse()
    {
        // Arrange
        var a = BaseUser();
        var b = BaseUser();
        b.PasswordHash = "differentHash";

        // Assert
        Assert.False(a.AreEqual(b));
    }

    [Fact]
    public void AreEqual_DifferentEmailConfirmed_ReturnsFalse()
    {
        // Arrange
        var a = BaseUser();
        var b = BaseUser();
        b.EmailConfirmed = false;

        // Assert
        Assert.False(a.AreEqual(b));
    }

    [Fact]
    public void AreEqual_DifferentPhoneNumber_ReturnsFalse()
    {
        // Arrange
        var a = BaseUser();
        var b = BaseUser();
        b.PhoneNumber = "9999999999";

        // Assert
        Assert.False(a.AreEqual(b));
    }

    [Fact]
    public void AreEqual_DifferentAuthenticatorKey_ReturnsFalse()
    {
        // Arrange
        var a = BaseUser();
        var b = BaseUser();
        b.AuthenticatorKey = "differentKey";

        // Assert
        Assert.False(a.AreEqual(b));
    }

    [Fact]
    public void AreEqual_DifferentRecoveryCodes_ReturnsFalse()
    {
        // Arrange
        var a = BaseUser();
        var b = BaseUser();
        b.RecoveryCodes = "newcode";

        // Assert
        Assert.False(a.AreEqual(b));
    }

    [Fact]
    public void AreEqual_DifferentTwoFactorEnabled_ReturnsFalse()
    {
        // Arrange
        var a = BaseUser();
        var b = BaseUser();
        b.TwoFactorEnabled = true;

        // Assert
        Assert.False(a.AreEqual(b));
    }

    [Fact]
    public void AreEqual_NullEmailBothSides_ReturnsTrue()
    {
        // Arrange
        var a = BaseUser();
        var b = BaseUser();
        a.Email = null;
        b.Email = null;

        // Assert
        Assert.True(a.AreEqual(b));
    }

    [Fact]
    public void AreEqual_NullVsNonNull_ReturnsFalse()
    {
        // Arrange
        var a = BaseUser();
        var b = BaseUser();
        a.Email = null;

        // Assert
        Assert.False(a.AreEqual(b));
    }
}
