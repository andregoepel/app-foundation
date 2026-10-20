using System.Text.Json;
using AndreGoepel.AppFoundation.Sonar;
using Xunit;

namespace AndreGoepel.AppFoundation.Sonar.Tests;

public sealed class MainPolicyTests
{
    [Fact]
    public async Task Fingerprint_ReviewedStandbyPolicy_MatchesExpectedDigestOnEveryPlatform()
    {
        // Arrange
        var directory = new DirectoryInfo(AppContext.BaseDirectory);
        while (
            directory is not null
            && !File.Exists(Path.Combine(directory.FullName, "scripts/sonar/main-policy.json"))
        )
            directory = directory.Parent;
        Assert.NotNull(directory);
        var policy = JsonSerializer.Deserialize<MainPolicy>(
            await File.ReadAllTextAsync(
                Path.Combine(directory.FullName, "scripts/sonar/main-policy.json"),
                TestContext.Current.CancellationToken
            ),
            JsonFormat.Options
        )!;

        // Act
        var fingerprint = policy.Fingerprint();

        // Assert
        Assert.Equal(
            "af25a521b55eb2c186cda3ff688bd96d672ac997cf424c824bb73a97065ab256",
            fingerprint
        );
    }
}
