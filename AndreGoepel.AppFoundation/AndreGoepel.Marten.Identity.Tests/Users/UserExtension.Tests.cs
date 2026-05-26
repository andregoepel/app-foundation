using AndreGoepel.Marten.Identity.Users;

namespace AndreGoepel.Marten.Identity.Tests.Users;

public class UserExtensionTests
{
    private static readonly DateTimeOffset _lockoutEnd = new(2026, 1, 1, 12, 0, 0, TimeSpan.Zero);

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
            Deletable = true,
            LockoutEnabled = true,
            LockoutEnd = _lockoutEnd,
            AccessFailedCount = 0,
        };

    [Fact]
    public void AreEqual_IdenticalUsers_ReturnsTrue()
    {
        // Arrange
        var a = BaseUser();
        var b = BaseUser();

        // Act
        var result = a.AreEqual(b);

        // Assert
        Assert.True(result);
    }

    [Fact]
    public void AreEqual_SameInstance_ReturnsTrue()
    {
        // Arrange
        var user = BaseUser();

        // Act
        var result = user.AreEqual(user);

        // Assert
        Assert.True(result);
    }

    [Fact]
    public void AreEqual_DifferentEmail_ReturnsFalse()
    {
        // Arrange
        var a = BaseUser();
        var b = BaseUser();
        b.Email = "bob@example.com";

        // Act
        var result = a.AreEqual(b);

        // Assert
        Assert.False(result);
    }

    [Fact]
    public void AreEqual_DifferentUserName_ReturnsFalse()
    {
        // Arrange
        var a = BaseUser();
        var b = BaseUser();
        b.UserName = "bob";

        // Act
        var result = a.AreEqual(b);

        // Assert
        Assert.False(result);
    }

    [Fact]
    public void AreEqual_DifferentPasswordHash_ReturnsFalse()
    {
        // Arrange
        var a = BaseUser();
        var b = BaseUser();
        b.PasswordHash = "differentHash";

        // Act
        var result = a.AreEqual(b);

        // Assert
        Assert.False(result);
    }

    [Fact]
    public void AreEqual_DifferentEmailConfirmed_ReturnsFalse()
    {
        // Arrange
        var a = BaseUser();
        var b = BaseUser();
        b.EmailConfirmed = false;

        // Act
        var result = a.AreEqual(b);

        // Assert
        Assert.False(result);
    }

    [Fact]
    public void AreEqual_DifferentPhoneNumber_ReturnsFalse()
    {
        // Arrange
        var a = BaseUser();
        var b = BaseUser();
        b.PhoneNumber = "9999999999";

        // Act
        var result = a.AreEqual(b);

        // Assert
        Assert.False(result);
    }

    [Fact]
    public void AreEqual_DifferentAuthenticatorKey_ReturnsFalse()
    {
        // Arrange
        var a = BaseUser();
        var b = BaseUser();
        b.AuthenticatorKey = "differentKey";

        // Act
        var result = a.AreEqual(b);

        // Assert
        Assert.False(result);
    }

    [Fact]
    public void AreEqual_DifferentRecoveryCodes_ReturnsFalse()
    {
        // Arrange
        var a = BaseUser();
        var b = BaseUser();
        b.RecoveryCodes = "newcode";

        // Act
        var result = a.AreEqual(b);

        // Assert
        Assert.False(result);
    }

    [Fact]
    public void AreEqual_DifferentTwoFactorEnabled_ReturnsFalse()
    {
        // Arrange
        var a = BaseUser();
        var b = BaseUser();
        b.TwoFactorEnabled = true;

        // Act
        var result = a.AreEqual(b);

        // Assert
        Assert.False(result);
    }

    [Fact]
    public void AreEqual_DifferentDeletable_ReturnsFalse()
    {
        // Arrange
        var a = BaseUser();
        var b = BaseUser();
        b.Deletable = false;

        // Act
        var result = a.AreEqual(b);

        // Assert
        Assert.False(result);
    }

    [Fact]
    public void AreEqual_DifferentLockoutEnabled_ReturnsFalse()
    {
        // Arrange
        var a = BaseUser();
        var b = BaseUser();
        b.LockoutEnabled = false;

        // Act
        var result = a.AreEqual(b);

        // Assert
        Assert.False(result);
    }

    [Fact]
    public void AreEqual_DifferentLockoutEnd_ReturnsFalse()
    {
        // Arrange
        var a = BaseUser();
        var b = BaseUser();
        b.LockoutEnd = _lockoutEnd.AddHours(1);

        // Act
        var result = a.AreEqual(b);

        // Assert
        Assert.False(result);
    }

    [Fact]
    public void AreEqual_DifferentAccessFailedCount_ReturnsFalse()
    {
        // Arrange
        var a = BaseUser();
        var b = BaseUser();
        b.AccessFailedCount = 3;

        // Act
        var result = a.AreEqual(b);

        // Assert
        Assert.False(result);
    }

    [Fact]
    public void AreEqual_NullEmailBothSides_ReturnsTrue()
    {
        // Arrange
        var a = BaseUser();
        var b = BaseUser();
        a.Email = null;
        b.Email = null;

        // Act
        var result = a.AreEqual(b);

        // Assert
        Assert.True(result);
    }

    [Fact]
    public void AreEqual_NullVsNonNull_ReturnsFalse()
    {
        // Arrange
        var a = BaseUser();
        var b = BaseUser();
        a.Email = null;

        // Act
        var result = a.AreEqual(b);

        // Assert
        Assert.False(result);
    }
}
