using System.Text.Json;
using AndreGoepel.AppFoundation.Sonar;
using Xunit;

namespace AndreGoepel.AppFoundation.Sonar.Tests;

public sealed class MainPolicyTests
{
    [Fact]
    public async Task Fingerprint_ReviewedPolicy_MatchesExistingLinuxReceiptOnEveryPlatform()
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
            "b28c6b8b6c18553f3013cbeb209427d037474b27bad989a92fc36365ce181639",
            fingerprint
        );
    }
}
