namespace AndreGoepel.AppFoundation.E2ETests.Infrastructure;

/// <summary>Shared constants and generators so tests don't reinvent credentials.</summary>
public static class TestData
{
    /// <summary>Canonical administrator created by the one-time Setup flow and reused across admin tests.</summary>
    public const string AdminEmail = "admin@e2e.test";

    /// <summary>Password used for the admin and, by default, for generated users. Meets Identity complexity rules.</summary>
    public const string DefaultPassword = "Passw0rd!";

    /// <summary>A second valid password used when a flow needs to change away from the default.</summary>
    public const string AlternatePassword = "Ch4nged!Pass";

    /// <summary>Produces a unique, valid email so parallel-ish tests never collide on identity.</summary>
    public static string NewEmail(string prefix = "user") =>
        $"{prefix}-{Guid.NewGuid():N}@e2e.test";
}
