using System.Net;
using System.Text.Json;
using System.Text.Json.Nodes;
using AndreGoepel.AppFoundation.Sonar;
using Xunit;

namespace AndreGoepel.AppFoundation.Sonar.Tests;

public sealed class NuGetAcceptanceTests
{
    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task Run_CompleteEvidence_ValidatesAllPackagesWithoutPublication(bool tagged)
    {
        // Arrange
        using var fixture = new NuGetAcceptanceFixture();
        await fixture.InitializeAsync(TestContext.Current.CancellationToken);
        fixture.SetTag(MainFixture.Sha);

        // Act
        var result = await fixture.RunAsync(TestContext.Current.CancellationToken, tagged);

        // Assert
        Assert.True(result.IsSuccess, result.Error);
        Assert.Single(fixture.Manifests());
        Assert.Equal(6, fixture.PackageCommands);
        using var manifest = JsonDocument.Parse(
            await File.ReadAllTextAsync(result.Value!, TestContext.Current.CancellationToken)
        );
        var receipt = manifest.RootElement;
        Assert.True(receipt.GetProperty("dryRun").GetBoolean());
        Assert.False(receipt.GetProperty("releaseEligible").GetBoolean());
        Assert.Equal(MainFixture.Sha, receipt.GetProperty("sha").GetString());
        Assert.Equal(100, receipt.GetProperty("readinessRunId").GetInt64());
        Assert.Equal(1, receipt.GetProperty("readinessAttempt").GetInt32());
        var packages = receipt.GetProperty("packages").EnumerateArray().ToArray();
        Assert.Equal(12, packages.Length);
        foreach (var id in NuGetRelease.Packages)
        {
            var pair = packages.Where(p => p.GetProperty("packageId").GetString() == id).ToArray();
            Assert.Equal(2, pair.Length);
            Assert.Contains(pair, p => p.GetProperty("file").GetString() == id + ".2.2.0.nupkg");
            Assert.Contains(pair, p => p.GetProperty("file").GetString() == id + ".2.2.0.snupkg");
        }
        Assert.All(
            packages,
            package => Assert.Equal(NuGetRelease.Feed, package.GetProperty("feed").GetString())
        );
        var firstBuild = fixture.Events.FindIndex(entry =>
            entry.StartsWith("command:dotnet clean", StringComparison.Ordinal)
        );
        var lastPack = fixture.Events.FindLastIndex(entry =>
            entry.StartsWith("command:dotnet pack", StringComparison.Ordinal)
        );
        var gateChecks = fixture
            .Events.Select((entry, index) => (entry, index))
            .Where(item =>
                item.entry.Contains("qualitygates/project_status", StringComparison.Ordinal)
            )
            .ToArray();
        Assert.Equal(2, gateChecks.Length);
        Assert.True(gateChecks[0].index < firstBuild);
        Assert.True(gateChecks[1].index > lastPack);
        fixture.AssertNoPublication();
    }

    [Theory]
    [InlineData("failed-gate")]
    [InlineData("wrong-sha")]
    [InlineData("unmerged-source")]
    [InlineData("dirty-source")]
    [InlineData("stale-evidence")]
    [InlineData("missing-evidence")]
    [InlineData("missing-e2e")]
    [InlineData("unknown-workflow")]
    [InlineData("unknown-revision")]
    [InlineData("old-attempt")]
    [InlineData("new-failed-producer")]
    [InlineData("source-tampering")]
    [InlineData("summary-tampering")]
    [InlineData("policy-change")]
    [InlineData("baseline-change")]
    [InlineData("wrong-tag")]
    public async Task Run_InvalidInitialEvidence_StopsBeforePreparation(string fault)
    {
        // Arrange
        using var fixture = new NuGetAcceptanceFixture();
        await fixture.InitializeAsync(TestContext.Current.CancellationToken);
        fixture.SetTag(MainFixture.Sha);
        ApplyFault(fixture, fault);
        fixture.SealArtifacts();

        // Act
        var result = await fixture.RunAsync(TestContext.Current.CancellationToken, tagged: true);

        // Assert
        Assert.True(result.IsFailure, fault);
        Assert.Empty(fixture.Builder.Commands);
        Assert.Empty(fixture.Manifests());
        fixture.AssertNoPublication();
    }

    [Theory]
    [InlineData("failed-gate")]
    [InlineData("unmerged-source")]
    [InlineData("stale-evidence")]
    [InlineData("missing-evidence")]
    [InlineData("missing-e2e")]
    [InlineData("old-attempt")]
    [InlineData("new-failed-producer")]
    [InlineData("source-tampering")]
    [InlineData("policy-change")]
    [InlineData("baseline-change")]
    [InlineData("wrong-tag")]
    public async Task Run_EvidenceChangesDuringPacking_RejectsPreparedPackages(string fault)
    {
        // Arrange
        using var fixture = new NuGetAcceptanceFixture();
        await fixture.InitializeAsync(TestContext.Current.CancellationToken);
        fixture.SetTag(MainFixture.Sha);
        fixture.Builder.OnPacked = _ =>
        {
            if (fixture.PackageCommands == 6)
                ApplyFault(fixture, fault);
        };

        // Act
        var result = await fixture.RunAsync(TestContext.Current.CancellationToken, tagged: true);

        // Assert
        Assert.True(result.IsFailure, fault);
        Assert.Equal(6, fixture.PackageCommands);
        Assert.Empty(fixture.Manifests());
        fixture.AssertNoPublication();
    }

    [Theory]
    [InlineData("changed-inputs")]
    [InlineData("build")]
    [InlineData("vulnerability")]
    [InlineData("missing-tests")]
    [InlineData("test")]
    [InlineData("skipped-test")]
    public async Task Run_VersionedPreparationFails_StopsBeforePacking(string fault)
    {
        // Arrange
        using var fixture = new NuGetAcceptanceFixture(fault);
        await fixture.InitializeAsync(TestContext.Current.CancellationToken);

        // Act
        var result = await fixture.RunAsync(TestContext.Current.CancellationToken);

        // Assert
        Assert.True(result.IsFailure, fault);
        Assert.NotEmpty(fixture.Builder.Commands);
        Assert.Equal(0, fixture.PackageCommands);
        Assert.Empty(fixture.Manifests());
        fixture.AssertNoPublication();
    }

    [Theory]
    [InlineData("missing-symbols")]
    [InlineData("extra-package")]
    [InlineData("wrong-version")]
    [InlineData("wrong-commit")]
    [InlineData("wrong-dependency")]
    [InlineData("wrong-binary")]
    [InlineData("wrong-symbols")]
    [InlineData("duplicate-entry")]
    public async Task Run_InvalidPackageSet_RejectsWholeBatch(string fault)
    {
        // Arrange
        using var fixture = new NuGetAcceptanceFixture();
        await fixture.InitializeAsync(TestContext.Current.CancellationToken);
        fixture.Builder.PackageFault = fault;

        // Act
        var result = await fixture.RunAsync(TestContext.Current.CancellationToken);

        // Assert
        Assert.True(result.IsFailure, fault);
        Assert.Equal(6, fixture.PackageCommands);
        Assert.Empty(fixture.Manifests());
        fixture.AssertNoPublication();
    }

    [Theory]
    [InlineData("*.nupkg")]
    [InlineData("*.snupkg")]
    public async Task Run_PackageChangesDuringFinalVerification_RejectsManifest(string pattern)
    {
        // Arrange
        using var fixture = new NuGetAcceptanceFixture();
        await fixture.InitializeAsync(TestContext.Current.CancellationToken);
        fixture.OnRequest = endpoint =>
        {
            if (fixture.PackageCommands == 6 && endpoint == "system/status")
                File.WriteAllText(
                    Directory.GetFiles(fixture.Evidence.Root, pattern, SearchOption.AllDirectories)[
                        0
                    ],
                    "tampered"
                );
        };

        // Act
        var result = await fixture.RunAsync(TestContext.Current.CancellationToken);

        // Assert
        Assert.True(result.IsFailure);
        Assert.Equal("Prepared packages changed during final verification.", result.Error);
        Assert.Empty(fixture.Manifests());
        fixture.AssertNoPublication();
    }

    [Theory]
    [InlineData("actions/runs/100", HttpStatusCode.Unauthorized)]
    [InlineData("actions/runs/100", HttpStatusCode.Forbidden)]
    [InlineData("actions/runs/100", HttpStatusCode.TooManyRequests)]
    [InlineData("system/status", HttpStatusCode.ServiceUnavailable)]
    public async Task Run_ApiFailure_StopsBeforePreparation(string endpoint, HttpStatusCode status)
    {
        // Arrange
        using var fixture = new NuGetAcceptanceFixture();
        await fixture.InitializeAsync(TestContext.Current.CancellationToken);
        fixture.FailedEndpoint = endpoint;
        fixture.FailureStatus = status;

        // Act
        var error = await Record.ExceptionAsync(() =>
            fixture.RunAsync(TestContext.Current.CancellationToken)
        );

        // Assert
        Assert.IsType<HttpRequestException>(error);
        Assert.Empty(fixture.Builder.Commands);
        Assert.Empty(fixture.Manifests());
        fixture.AssertNoPublication();
    }

    [Theory]
    [InlineData("corrupt-archive")]
    [InlineData("foreign-storage")]
    [InlineData("malformed-json")]
    [InlineData("cancelled")]
    public async Task Run_UnreadableEvidence_StopsBeforePreparation(string fault)
    {
        // Arrange
        using var fixture = new NuGetAcceptanceFixture();
        await fixture.InitializeAsync(TestContext.Current.CancellationToken);
        switch (fault)
        {
            case "corrupt-archive":
                fixture.CorruptArchive();
                break;
            case "foreign-storage":
                fixture.StorageHost = "untrusted.example";
                break;
            case "malformed-json":
                fixture.MalformedEndpoint = "actions/runs/100";
                break;
            case "cancelled":
                fixture.CancelledEndpoint = "actions/runs/100";
                break;
        }

        // Act
        var error = await Record.ExceptionAsync(() =>
            fixture.RunAsync(TestContext.Current.CancellationToken)
        );

        // Assert
        if (fault == "malformed-json")
            Assert.IsType<JsonException>(error, exactMatch: false);
        else if (fault == "cancelled")
            Assert.IsType<OperationCanceledException>(error, exactMatch: false);
        else
            Assert.IsType<InvalidDataException>(error);
        Assert.Empty(fixture.Builder.Commands);
        Assert.Empty(fixture.Manifests());
        fixture.AssertNoPublication();
    }

    [Theory]
    [InlineData("stale-evidence")]
    [InlineData("missing-evidence")]
    public async Task Run_PreviousSuccessfulManifest_DoesNotReplaceFreshVerification(string fault)
    {
        // Arrange
        using var fixture = new NuGetAcceptanceFixture();
        await fixture.InitializeAsync(TestContext.Current.CancellationToken);
        var previous = await fixture.RunAsync(TestContext.Current.CancellationToken);
        Assert.True(previous.IsSuccess, previous.Error);
        var bytes = await File.ReadAllBytesAsync(
            previous.Value!,
            TestContext.Current.CancellationToken
        );
        var builds = fixture.Builder.Commands.Count;
        ApplyFault(fixture, fault);

        // Act
        var result = await fixture.RunAsync(TestContext.Current.CancellationToken);

        // Assert
        Assert.True(result.IsFailure);
        Assert.Equal(builds, fixture.Builder.Commands.Count);
        Assert.Equal(Path.GetFullPath(previous.Value!), Assert.Single(fixture.Manifests()));
        Assert.Equal(
            bytes,
            await File.ReadAllBytesAsync(previous.Value!, TestContext.Current.CancellationToken)
        );
        fixture.AssertNoPublication();
    }

    private static void ApplyFault(NuGetAcceptanceFixture fixture, string fault)
    {
        var evidence = fixture.Evidence;
        switch (fault)
        {
            case "failed-gate":
                evidence.Main.Responses["qualitygates/project_status"]["projectStatus"]!["status"] =
                    "ERROR";
                break;
            case "wrong-sha":
                evidence.Head = new string('f', 40);
                break;
            case "unmerged-source":
                evidence.Main.Responses["git/ref/heads/main"]["object"]!["sha"] = new string(
                    'f',
                    40
                );
                break;
            case "dirty-source":
                evidence.Dirty = true;
                break;
            case "stale-evidence":
                fixture.Clock.Now = evidence.Main.Now.AddHours(1);
                break;
            case "missing-evidence":
                evidence.ArtifactInventory["artifacts"] = new JsonArray();
                evidence.ArtifactInventory["total_count"] = 0;
                break;
            case "missing-e2e":
                evidence.Main.Responses[
                    $"actions/workflows/20/runs?branch=main&event=push&head_sha={MainFixture.Sha}&per_page=100"
                ] = MainFixture.Node(
                    new { total_count = 0, workflow_runs = Array.Empty<object>() }
                );
                break;
            case "unknown-workflow":
                evidence.Producer["path"] = ".github/workflows/unknown.yml";
                break;
            case "unknown-revision":
                evidence.Receipt["workflowRevision"] = new string('f', 40);
                break;
            case "old-attempt":
                evidence.Producer["run_attempt"] = 2;
                break;
            case "new-failed-producer":
                evidence.Main.Responses[ReleaseFixture.ProducerInventory] = MainFixture.Node(
                    new
                    {
                        total_count = 2,
                        workflow_runs = new[] { new { id = 101 }, new { id = 100 } },
                    }
                );
                evidence.Main.Responses["actions/runs/101"] = evidence.Producer.DeepClone();
                evidence.Main.Responses["actions/runs/101"]["conclusion"] = "failure";
                break;
            case "source-tampering":
                File.WriteAllText(
                    Path.Combine(evidence.Root, "injected.cs"),
                    "class Injected {}\n"
                );
                break;
            case "summary-tampering":
                evidence.Summary["sdk"] = "changed";
                break;
            case "policy-change":
                evidence.Main.Responses["qualitygates/show"]["conditions"]![0]!["error"] = "10";
                break;
            case "baseline-change":
                evidence.Main.Responses["new_code_periods/show"]["value"] = "changed";
                break;
            case "wrong-tag":
                fixture.SetTag(new string('f', 40));
                break;
            default:
                throw new ArgumentOutOfRangeException(nameof(fault));
        }
    }
}
