using Microsoft.AspNetCore.Identity;

namespace AndreGoepel.Marten.Identity.Users;

public sealed class UserPasskey : IEquatable<UserPasskey>
{
    public string CredentialId => Convert.ToBase64String(PasskeyInfo.CredentialId);
    public required UserPasskeyInfo PasskeyInfo { get; set; }

    public bool Equals(UserPasskey? other) =>
        other is not null && StringComparer.Ordinal.Equals(CredentialId, other.CredentialId);

    public override bool Equals(object? obj) => obj is UserPasskey d && Equals(d);

    public override int GetHashCode() => StringComparer.Ordinal.GetHashCode(CredentialId);
}
