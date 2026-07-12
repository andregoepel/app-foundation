using OtpNet;

namespace AndreGoepel.AppFoundation.E2ETests.Infrastructure;

/// <summary>Generates authenticator codes from the shared key the EnableAuthenticator page displays.</summary>
public static class Totp
{
    /// <summary>
    /// Computes the current 6-digit code for a base32 shared key. The UI shows the key in
    /// space-separated groups, so whitespace is stripped and the value upper-cased first.
    /// </summary>
    public static string Compute(string sharedKey)
    {
        var normalized = sharedKey.Replace(" ", string.Empty).ToUpperInvariant();
        var totp = new OtpNet.Totp(Base32Encoding.ToBytes(normalized));
        return totp.ComputeTotp();
    }
}
