namespace AndreGoepel.AppFoundation.E2ETests.Infrastructure;

/// <summary>
/// Installs a Chromium CDP virtual authenticator so passkey (WebAuthn) ceremonies complete without a
/// physical device. It auto-simulates user presence/verification, so create/get calls just succeed.
/// </summary>
public sealed class VirtualAuthenticator
{
    private readonly ICDPSession _session;

    private VirtualAuthenticator(ICDPSession session) => _session = session;

    public static async Task<VirtualAuthenticator> EnableAsync(IBrowserContext context, IPage page)
    {
        var session = await context.NewCDPSessionAsync(page);
        await session.SendAsync("WebAuthn.enable");
        await session.SendAsync(
            "WebAuthn.addVirtualAuthenticator",
            new Dictionary<string, object>
            {
                ["options"] = new Dictionary<string, object>
                {
                    ["protocol"] = "ctap2",
                    ["transport"] = "internal",
                    ["hasResidentKey"] = true,
                    ["hasUserVerification"] = true,
                    ["isUserVerified"] = true,
                    ["automaticPresenceSimulation"] = true,
                },
            }
        );

        return new VirtualAuthenticator(session);
    }

    public async Task DisableAsync()
    {
        try
        {
            await _session.SendAsync("WebAuthn.disable");
        }
        catch
        {
            // Circuit/context may already be torn down; nothing to clean up then.
        }
    }
}
