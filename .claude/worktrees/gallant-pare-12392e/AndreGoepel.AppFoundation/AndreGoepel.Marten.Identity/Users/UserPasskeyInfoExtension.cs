using Microsoft.AspNetCore.Identity;

namespace AndreGoepel.Marten.Identity.Users;

internal static class UserPasskeyInfoExtension
{
    public static bool OnlyCountChanged(this UserPasskeyInfo @this, UserPasskeyInfo other) =>
        @this.CredentialId.SequenceEqual(other.CredentialId)
        && @this.PublicKey.SequenceEqual(other.PublicKey)
        && @this.CreatedAt == other.CreatedAt
        && @this.Transports.SequenceEqual(other.Transports)
        && @this.IsUserVerified == other.IsUserVerified
        && @this.IsBackupEligible == other.IsBackupEligible
        && @this.IsBackedUp == other.IsBackedUp
        && @this.Name == other.Name
        && @this.AttestationObject.SequenceEqual(other.AttestationObject)
        && @this.ClientDataJson.SequenceEqual(other.ClientDataJson);
}
