namespace AndreGoepel.Marten.Identity.Users;

internal static class UserExtension
{
    public static bool AreEqual(this User @this, User other) =>
        @this.Email == other.Email
        && @this.UserName == other.UserName
        && @this.PasswordHash == other.PasswordHash
        && @this.EmailConfirmed == other.EmailConfirmed
        && @this.PhoneNumber == other.PhoneNumber
        && @this.AuthenticatorKey == other.AuthenticatorKey
        && @this.RecoveryCodes == other.RecoveryCodes
        && @this.TwoFactorEnabled == other.TwoFactorEnabled
        && @this.Deletable == other.Deletable
        && @this.LockoutEnabled == other.LockoutEnabled
        && @this.LockoutEnd == other.LockoutEnd
        && @this.AccessFailedCount == other.AccessFailedCount;
}
