using AndreGoepel.Marten.Identity.Users;

namespace AndreGoepel.Marten.Identity.Tests.Users;

public class UserExtensionTests
{
    private static User BaseUser() => new()
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
        var a = BaseUser();
        var b = BaseUser();

        Assert.True(a.AreEqual(b));
    }

    [Fact]
    public void AreEqual_SameInstance_ReturnsTrue()
    {
        var user = BaseUser();

        Assert.True(user.AreEqual(user));
    }

    [Fact]
    public void AreEqual_DifferentEmail_ReturnsFalse()
    {
        var a = BaseUser();
        var b = BaseUser();
        b.Email = "bob@example.com";

        Assert.False(a.AreEqual(b));
    }

    [Fact]
    public void AreEqual_DifferentUserName_ReturnsFalse()
    {
        var a = BaseUser();
        var b = BaseUser();
        b.UserName = "bob";

        Assert.False(a.AreEqual(b));
    }

    [Fact]
    public void AreEqual_DifferentPasswordHash_ReturnsFalse()
    {
        var a = BaseUser();
        var b = BaseUser();
        b.PasswordHash = "differentHash";

        Assert.False(a.AreEqual(b));
    }

    [Fact]
    public void AreEqual_DifferentEmailConfirmed_ReturnsFalse()
    {
        var a = BaseUser();
        var b = BaseUser();
        b.EmailConfirmed = false;

        Assert.False(a.AreEqual(b));
    }

    [Fact]
    public void AreEqual_DifferentPhoneNumber_ReturnsFalse()
    {
        var a = BaseUser();
        var b = BaseUser();
        b.PhoneNumber = "9999999999";

        Assert.False(a.AreEqual(b));
    }

    [Fact]
    public void AreEqual_DifferentAuthenticatorKey_ReturnsFalse()
    {
        var a = BaseUser();
        var b = BaseUser();
        b.AuthenticatorKey = "differentKey";

        Assert.False(a.AreEqual(b));
    }

    [Fact]
    public void AreEqual_DifferentRecoveryCodes_ReturnsFalse()
    {
        var a = BaseUser();
        var b = BaseUser();
        b.RecoveryCodes = "newcode";

        Assert.False(a.AreEqual(b));
    }

    [Fact]
    public void AreEqual_DifferentTwoFactorEnabled_ReturnsFalse()
    {
        var a = BaseUser();
        var b = BaseUser();
        b.TwoFactorEnabled = true;

        Assert.False(a.AreEqual(b));
    }

    [Fact]
    public void AreEqual_NullEmailBothSides_ReturnsTrue()
    {
        var a = BaseUser();
        var b = BaseUser();
        a.Email = null;
        b.Email = null;

        Assert.True(a.AreEqual(b));
    }

    [Fact]
    public void AreEqual_NullVsNonNull_ReturnsFalse()
    {
        var a = BaseUser();
        var b = BaseUser();
        a.Email = null;

        Assert.False(a.AreEqual(b));
    }
}
