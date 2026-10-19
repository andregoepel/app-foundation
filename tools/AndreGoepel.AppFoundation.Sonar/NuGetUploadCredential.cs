namespace AndreGoepel.AppFoundation.Sonar;

internal sealed class NuGetUploadCredential(string key, DateTimeOffset expiresAt) : IDisposable
{
    internal string Key { get; private set; } = key;

    internal bool IsUsable(DateTimeOffset now) =>
        !string.IsNullOrWhiteSpace(Key)
        && !Key.Any(char.IsControl)
        && expiresAt > now
        && expiresAt <= now.AddHours(1);

    public override string ToString() => "NuGetUploadCredential [redacted]";

    public void Dispose() => Key = "";
}
