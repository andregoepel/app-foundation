namespace AndreGoepel.AppFoundation.E2ETests.Infrastructure;

/// <summary>Shared constants and generators so tests don't reinvent credentials.</summary>
public static class TestData
{
    /// <summary>Canonical administrator created by the one-time Setup flow and reused across admin tests.</summary>
    public const string AdminEmail = "admin@e2e.test";

    /// <summary>
    /// Password for the admin and, by default, generated users. Meets Identity complexity rules and
    /// the Setup form's 12-character minimum.
    /// </summary>
    public const string DefaultPassword = "E2e-Passw0rd!23";

    /// <summary>A second valid password (also 12+ chars) used when a flow needs to change away from the default.</summary>
    public const string AlternatePassword = "Ch4nged!Passw0rd";

    /// <summary>Produces a unique, valid email so parallel-ish tests never collide on identity.</summary>
    public static string NewEmail(string prefix = "user") =>
        $"{prefix}-{Guid.NewGuid():N}@e2e.test";
}
