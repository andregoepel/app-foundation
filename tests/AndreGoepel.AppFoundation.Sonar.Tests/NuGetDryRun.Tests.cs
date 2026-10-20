using System.Text.Json;
using AndreGoepel.AppFoundation.Sonar;
using AndreGoepel.Core;
using Xunit;

namespace AndreGoepel.AppFoundation.Sonar.Tests;

public sealed class NuGetDryRunTests
{
    [Theory]
    [InlineData("")]
    [InlineData("initial-gate")]
    [InlineData("package-build")]
    [InlineData("final-gate")]
    [InlineData("changed-attempt")]
    [InlineData("tampered-package")]
    public async Task Run_TrustBoundaries_OnlyWritesDryRunAfterFinalVerification(string fault)
    {
        // Arrange
        using var fixture = new ReleaseFixture();
        await fixture.InitializeAsync(TestContext.Current.CancellationToken);
        var boundary = new DryRunBoundary(fixture, fault);

        // Act
        var result = await new NuGetDryRun(boundary, boundary).RunAsync(
            fixture.Release,
            TestContext.Current.CancellationToken
        );

        // Assert
        Assert.Equal(fault.Length == 0, result.IsSuccess);
        Assert.Equal(fault == "initial-gate" ? 0 : 1, boundary.Builds);
        var receipts = Directory.Exists(Path.Combine(fixture.Root, "artifacts"))
            ? Directory.GetFiles(
                Path.Combine(fixture.Root, "artifacts"),
                "dry-run.json",
                SearchOption.AllDirectories
            )
            : [];
        if (fault.Length == 0)
        {
            Assert.Single(receipts);
            using var json = JsonDocument.Parse(
                await File.ReadAllTextAsync(result.Value!, TestContext.Current.CancellationToken)
            );
            Assert.True(json.RootElement.GetProperty("dryRun").GetBoolean());
            Assert.False(json.RootElement.GetProperty("releaseEligible").GetBoolean());
            Assert.Equal(12, json.RootElement.GetProperty("packages").GetArrayLength());
        }
        else
            Assert.Empty(receipts);
    }

    private sealed class DryRunBoundary(ReleaseFixture fixture, string fault)
        : IReleaseReadiness,
            INuGetPackageBuilder
    {
        internal int Builds { get; private set; }
        private int checks;

        public Task<Result<ReadinessReceipt>> VerifyAsync(
            ReleaseCandidate release,
            CancellationToken ct
        )
        {
            checks++;
            if ((checks == 1 && fault == "initial-gate") || (checks == 2 && fault == "final-gate"))
                return Task.FromResult(Result.Fail<ReadinessReceipt>("Blocked"));
            var receipt = fixture.Receipt.Deserialize<ReadinessReceipt>(JsonFormat.Options)!;
            if (checks == 2 && fault == "tampered-package")
            {
                var path = Directory.GetFiles(fixture.Root, "*.nupkg", SearchOption.AllDirectories)[
                    0
                ];
                File.WriteAllText(path, "changed-after-pack");
            }
            if (checks == 2 && fault == "changed-attempt")
                receipt = receipt with { Run = receipt.Run with { Attempt = 2 } };
            return Task.FromResult(Result.Ok(receipt));
        }

        public async Task<Result<PackageEvidence[]>> BuildAsync(
            NuGetRelease release,
            string output,
            CancellationToken ct
        )
        {
            Builds++;
            if (fault == "package-build")
                return Result.Fail<PackageEvidence[]>("Build failed");
            var directory = Path.Combine(output, "packages");
            NuGetPackagesTests.CreatePackages(release, directory);
            return await NuGetPackages.InspectAsync(release, directory, ct);
        }
    }
}
