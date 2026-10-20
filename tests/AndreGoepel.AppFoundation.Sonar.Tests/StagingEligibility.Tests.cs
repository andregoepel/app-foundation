using System.Text.Json;
using System.Text.Json.Nodes;
using AndreGoepel.AppFoundation.Sonar;
using Xunit;

namespace AndreGoepel.AppFoundation.Sonar.Tests;

public sealed class StagingEligibilityTests
{
    [Fact]
    public async Task Harness_CompleteReadiness_RunsContractMatrixWithoutDeploymentAuthority()
    {
        // Arrange
        using var fixture = new ReleaseFixture();
        await fixture.InitializeAsync(TestContext.Current.CancellationToken);
        var harness = new StagingContractHarness(fixture.Verifier(), TimeProvider.System);

        // Act
        var report = await harness.RunAsync(fixture.Release, TestContext.Current.CancellationToken);

        // Assert
        Assert.True(report.Passed, JsonSerializer.Serialize(report));
        Assert.True(report.ActualReadinessAccepted);
        Assert.True(report.SyntheticDeploymentEvidence);
        Assert.True(report.DryRun);
        Assert.False(report.DeploymentEligible);
        Assert.Equal(StagingSyntheticEvidence.Scenarios.Length, report.Cases.Length);
        Assert.Single(report.Cases, c => c.Accepted);
        Assert.Equal(4, fixture.Downloads.Count);
    }

    [Theory]
    [InlineData("wrong-main")]
    [InlineData("missing-e2e")]
    [InlineData("absent-gate")]
    [InlineData("failed-gate")]
    [InlineData("expired")]
    [InlineData("unknown-readiness-workflow")]
    public async Task Verify_ActualReadinessVerifierRejectsFault_NeverReadsImageEvidence(
        string fault
    )
    {
        // Arrange
        using var fixture = new ReleaseFixture();
        await fixture.InitializeAsync(TestContext.Current.CancellationToken);
        var deployment = new StagingSyntheticEvidence(
            fixture.Receipt.Deserialize<ReadinessReceipt>(JsonFormat.Options)!,
            fixture.Main.Now
        );
        var reads = 0;
        deployment.OnRead = () => reads++;
        switch (fault)
        {
            case "wrong-main":
                fixture.Main.Responses["git/ref/heads/main"]["object"]!["sha"] = "changed";
                break;
            case "missing-e2e":
                fixture.Receipt["mandatoryWorkflows"]!.AsArray().RemoveAt(1);
                break;
            case "absent-gate":
                fixture.Main.Responses["qualitygates/project_status"]["projectStatus"]!["status"] =
                    "NONE";
                break;
            case "failed-gate":
                fixture.Main.Responses["qualitygates/project_status"]["projectStatus"]!["status"] =
                    "ERROR";
                break;
            case "expired":
                fixture.Receipt["expiresAt"] = fixture.Main.Now;
                break;
            case "unknown-readiness-workflow":
                fixture.Producer["workflow_id"] = 999;
                break;
        }
        var verifier = new StagingEligibility(
            fixture.Verifier(),
            deployment,
            StagingSyntheticEvidence.Policy,
            TimeProvider.System
        );

        // Act
        var result = await verifier.VerifyAsync(
            fixture.Release,
            deployment.Candidate,
            TestContext.Current.CancellationToken
        );

        // Assert
        Assert.True(result.IsFailure);
        Assert.Equal(0, reads);
    }

    [Theory]
    [InlineData("schema")]
    [InlineData("repository")]
    [InlineData("readiness-run")]
    [InlineData("analysis")]
    [InlineData("source")]
    [InlineData("build-run")]
    [InlineData("build-attempt")]
    [InlineData("generation")]
    [InlineData("tag")]
    public async Task Verify_UntrustedCandidate_Blocks(string fault)
    {
        // Arrange
        using var fixture = new ReleaseFixture();
        await fixture.InitializeAsync(TestContext.Current.CancellationToken);
        var ready = fixture.Receipt.Deserialize<ReadinessReceipt>(JsonFormat.Options)!;
        var deployment = new StagingSyntheticEvidence(ready, fixture.Main.Now);
        var candidate = deployment.Candidate;
        candidate = fault switch
        {
            "schema" => candidate with { SchemaVersion = 2 },
            "repository" => candidate with { Repository = "someone/fork" },
            "readiness-run" => candidate with { ReadinessRunId = 101 },
            "analysis" => candidate with { AnalysisId = "other" },
            "source" => candidate with { SourceSha256 = new string('f', 64) },
            "build-run" => candidate with { BuildRunId = 1 },
            "build-attempt" => candidate with { BuildAttempt = 2 },
            "generation" => candidate with { OperatorGeneration = -1 },
            _ => candidate,
        };
        var snapshot = deployment.Apply("valid", ref candidate);
        var release = new ReleaseCandidate(
            fixture.Root,
            MainFixture.Sha,
            100,
            fault == "tag" ? "v2.2.0" : null
        );

        // Act
        var result = await new StagingEligibility(
            snapshot,
            deployment,
            StagingSyntheticEvidence.Policy,
            TimeProvider.System
        ).VerifyAsync(release, candidate, TestContext.Current.CancellationToken);

        // Assert
        Assert.True(result.IsFailure);
    }

    [Theory]
    [InlineData("repository")]
    [InlineData("sha")]
    [InlineData("path")]
    [InlineData("event")]
    [InlineData("branch")]
    [InlineData("readiness-run")]
    [InlineData("analysis")]
    [InlineData("source")]
    [InlineData("archive")]
    [InlineData("registry-digest")]
    [InlineData("short-digest")]
    [InlineData("foreign-image")]
    [InlineData("uppercase-digest")]
    [InlineData("early-build")]
    [InlineData("future-build")]
    [InlineData("extended-expiry")]
    [InlineData("operator-target")]
    [InlineData("negative-generation")]
    [InlineData("negative-sequence")]
    [InlineData("production-selected")]
    public async Task Verify_IndependentEvidenceDisagrees_Blocks(string fault)
    {
        // Arrange
        using var fixture = new ReleaseFixture();
        await fixture.InitializeAsync(TestContext.Current.CancellationToken);
        var ready = fixture.Receipt.Deserialize<ReadinessReceipt>(JsonFormat.Options)!;
        var deployment = new StagingSyntheticEvidence(ready, fixture.Main.Now);
        var candidate = deployment.Candidate;
        var snapshot = deployment.Apply("valid", ref candidate);
        var build = deployment.Evidence.Build;
        var state = deployment.Evidence.Operator;
        switch (fault)
        {
            case "repository":
                build = build with { Repository = "someone/fork" };
                break;
            case "sha":
                build = build with { Sha = new string('f', 40) };
                break;
            case "path":
                build = build with { WorkflowPath = "synthetic/unknown.yml" };
                break;
            case "event":
                build = build with { Event = "pull_request" };
                break;
            case "branch":
                build = build with { Branch = "feature/unmerged" };
                break;
            case "readiness-run":
                build = build with { ReadinessRunId = 101 };
                break;
            case "analysis":
                build = build with { AnalysisId = "other" };
                break;
            case "source":
                build = build with { SourceSha256 = "other" };
                break;
            case "archive":
                build = build with { ArtifactSha256 = "not-a-hash" };
                break;
            case "registry-digest":
                build = build with { RegistryImage = "other" };
                break;
            case "short-digest":
            case "uppercase-digest":
            case "foreign-image":
                var image = fault switch
                {
                    "short-digest" => StagingSyntheticEvidence.Policy.ImageRepository
                        + "@sha256:abc",
                    "uppercase-digest" => StagingSyntheticEvidence.Policy.ImageRepository
                        + "@sha256:"
                        + new string('A', 64),
                    _ => "ghcr.io/someone/image@sha256:" + new string('a', 64),
                };
                candidate = candidate with { Image = image };
                build = build with { Image = image, RegistryImage = image };
                break;
            case "early-build":
                build = build with { StartedAt = ready.IssuedAt.AddSeconds(-1) };
                break;
            case "future-build":
                build = build with { StartedAt = fixture.Main.Now.AddSeconds(1) };
                break;
            case "extended-expiry":
                build = build with { ExpiresAt = ready.ExpiresAt.AddSeconds(1) };
                break;
            case "operator-target":
                state = state with { Target = "production" };
                break;
            case "negative-generation":
                state = state with { Generation = -1 };
                break;
            case "negative-sequence":
                state = state with { LastBuildSequence = -1 };
                break;
            case "production-selected":
                state = state with { ProductionSelected = true };
                break;
        }
        deployment.Evidence = new(build, state);

        // Act
        var result = await new StagingEligibility(
            snapshot,
            deployment,
            StagingSyntheticEvidence.Policy,
            TimeProvider.System
        ).VerifyAsync(fixture.Release, candidate, TestContext.Current.CancellationToken);

        // Assert
        Assert.True(result.IsFailure);
    }

    [Fact]
    public async Task Verify_GateChangesDuringImageRead_RejectsCandidate()
    {
        // Arrange
        using var fixture = new ReleaseFixture();
        await fixture.InitializeAsync(TestContext.Current.CancellationToken);
        var deployment = new StagingSyntheticEvidence(
            fixture.Receipt.Deserialize<ReadinessReceipt>(JsonFormat.Options)!,
            fixture.Main.Now
        );
        deployment.OnRead = () =>
            fixture.Main.Responses["qualitygates/project_status"]["projectStatus"]!["status"] =
                "ERROR";

        // Act
        var result = await new StagingEligibility(
            fixture.Verifier(),
            deployment,
            StagingSyntheticEvidence.Policy,
            TimeProvider.System
        ).VerifyAsync(fixture.Release, deployment.Candidate, TestContext.Current.CancellationToken);

        // Assert
        Assert.True(result.IsFailure);
        Assert.Contains("changed", result.Error);
    }

    [Fact]
    public async Task Harness_FailedActualReadiness_DoesNotSubstituteSyntheticSuccess()
    {
        // Arrange
        using var fixture = new ReleaseFixture();
        await fixture.InitializeAsync(TestContext.Current.CancellationToken);
        fixture.Main.Responses["qualitygates/project_status"]["projectStatus"]!["status"] = "ERROR";

        // Act
        var report = await new StagingContractHarness(
            fixture.Verifier(),
            TimeProvider.System
        ).RunAsync(fixture.Release, TestContext.Current.CancellationToken);

        // Assert
        Assert.False(report.Passed);
        Assert.False(report.ActualReadinessAccepted);
        Assert.False(report.DeploymentEligible);
        Assert.Empty(report.Cases);
        Assert.NotEmpty(report.ReadinessError!);
    }

    [Theory]
    [InlineData("valid")]
    [InlineData("sha")]
    [InlineData("run")]
    [InlineData("negative")]
    [InlineData("unknown-option")]
    [InlineData("missing")]
    public void Parse_StagingContractArguments_OnlyAcceptsFixedReadOnlyMode(string scenario)
    {
        // Arrange
        using var fixture = new RunnerFixture();
        string[] args =
        [
            "--root",
            fixture.Root,
            "--staging-contract",
            "--sha",
            MainFixture.Sha,
            "--readiness-run",
            "100",
        ];
        if (scenario == "sha")
            args[4] = "bad";
        if (scenario == "run")
            args[6] = "1.0";
        if (scenario == "negative")
            args[6] = "-1";
        if (scenario == "unknown-option")
            args = [.. args, "--deploy"];
        if (scenario == "missing")
            args = args[..^1];

        // Act
        var result = StagingCommand.Parse(args);

        // Assert
        Assert.Equal(scenario == "valid", result.IsSuccess);
    }
}
