using System.Text.Json;
using Microsoft.AspNetCore.DataProtection;

namespace AndreGoepel.Marten.Identity.Http;

/// <summary>
/// Encrypts and signs short-lived login handoff payloads (password,
/// 2FA code, recovery code) into URL-safe tokens. Replaces the in-memory
/// ConcurrentDictionary-based handoff. Tokens carry a TTL — expired ones
/// fail to unprotect, so the window for replay is bounded.
/// </summary>
public sealed class LoginTokenProtector(IDataProtectionProvider provider)
{
    private const string _purpose = "AndreGoepel.Marten.Identity.LoginHandoff";

    // Two minutes is plenty for the round-trip from the form-submit to
    // the cookie-write middleware call; long enough to absorb a slow
    // network, short enough that a leaked URL is mostly useless.
    private static readonly TimeSpan _lifetime = TimeSpan.FromMinutes(2);

    private readonly ITimeLimitedDataProtector _protector = provider
        .CreateProtector(_purpose)
        .ToTimeLimitedDataProtector();

    public string Protect<T>(T payload)
    {
        var json = JsonSerializer.Serialize(payload);
        return _protector.Protect(json, _lifetime);
    }

    public bool TryUnprotect<T>(string? token, out T payload)
    {
        payload = default!;
        if (string.IsNullOrEmpty(token))
            return false;
        try
        {
            var json = _protector.Unprotect(token);
            var value = JsonSerializer.Deserialize<T>(json);
            if (value is null)
                return false;
            payload = value;
            return true;
        }
        catch (Exception)
        {
            // CryptographicException for tampered/expired tokens,
            // JsonException for malformed payloads — both are equivalent
            // from the caller's perspective: "token is unusable".
            return false;
        }
    }
}
