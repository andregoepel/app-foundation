using System.Text.Json;
using AndreGoepel.AppFoundation.Sonar;
using AndreGoepel.Core;
using Xunit;

namespace AndreGoepel.AppFoundation.Sonar.Tests;

public sealed class MainAnalysisTrustTests
{
    [Fact]
    public async Task Check_SupersedingCommit_RejectsPreviouslySuccessfulEvidence()
    {
        // Arrange
        var fixture = new MainFixture();
        var github = fixture.GitHub();
        var initial = await github.ReadAsync(TestContext.Current.CancellationToken);
        var trust = new MainAnalysisTrust(
            fixture.Identity,
            github,
            initial.Value!,
            fixture.Sonar()
        );
        fixture.Responses["git/ref/heads/main"]["object"]!["sha"] = "newer";

        // Act
        var result = await trust.CheckAsync(TestContext.Current.CancellationToken);

        // Assert
        Assert.True(result.IsFailure);
    }

    [Fact]
    public async Task Check_MandatoryJobIdentityChanges_RejectsLateCompletion()
    {
        // Arrange
        var fixture = new MainFixture();
        var github = fixture.GitHub();
        var initial = await github.ReadAsync(TestContext.Current.CancellationToken);
        var trust = new MainAnalysisTrust(
            fixture.Identity,
            github,
            initial.Value!,
            fixture.Sonar()
        );
        fixture.Responses["actions/runs/300/attempts/1/jobs?per_page=100"]["jobs"]![0]!["id"] = 999;

        // Act
        var result = await trust.CheckAsync(TestContext.Current.CancellationToken);

        // Assert
        Assert.True(result.IsFailure);
    }

    [Fact]
    public async Task Verify_StableEvidenceAndExactAnalysis_Accepts()
    {
        // Arrange
        var fixture = new MainFixture();
        var github = fixture.GitHub();
        var initial = await github.ReadAsync(TestContext.Current.CancellationToken);
        var trust = new MainAnalysisTrust(
            fixture.Identity,
            github,
            initial.Value!,
            fixture.Sonar()
        );

        // Act
        var result = await trust.VerifyAsync(
            fixture.Summary(),
            MainFixture.Element(fixture.Latest()),
            TestContext.Current.CancellationToken
        );

        // Assert
        Assert.True(result.IsSuccess, result.Error);
        Assert.Equal(MainFixture.Sha, trust.Revision);
        Assert.Equal(fixture.Identity.BuildString, trust.BuildString);
    }

    [Theory]
    [InlineData("none", true)]
    [InlineData("url", false)]
    [InlineData("project", false)]
    [InlineData("server", false)]
    [InlineData("run", false)]
    public async Task ValidateTarget_OnlyPinnedCentralIdentity_Accepts(string change, bool accepted)
    {
        // Arrange
        var fixture = new MainFixture();
        var github = fixture.GitHub();
        var initial = await github.ReadAsync(TestContext.Current.CancellationToken);
        var trust = new MainAnalysisTrust(
            fixture.Identity,
            github,
            initial.Value!,
            fixture.Sonar()
        );
        var options = new AnalysisOptions(
            Path.GetTempPath(),
            new Uri(MainPolicy.SonarUrl),
            MainPolicy.Project,
            MainPolicy.ServerId,
            fixture.Identity.RunName
        );
        options = change switch
        {
            "url" => options with { Url = new Uri("http://localhost:9000") },
            "project" => options with { Project = "other" },
            "server" => options with { ServerId = "other" },
            "run" => options with { RunName = "other" },
            _ => options,
        };

        // Act
        var result = trust.ValidateTarget(options);

        // Assert
        Assert.Equal(accepted, result.IsSuccess);
        Assert.True(options.ValidateTarget().IsFailure);
    }

    [Theory]
    [InlineData(1, false)]
    [InlineData(2, false)]
    [InlineData(3, false)]
    [InlineData(4, true)]
    public async Task Run_TrustFailsAtBoundary_BlocksCompletionAndCleansScanner(
        int failAt,
        bool submitted
    )
    {
        // Arrange
        using var fixture = new RunnerFixture();
        var trust = new BoundaryTrust(failAt);

        // Act
        var result = await new AnalysisRunner(fixture, fixture, trust).RunAsync(
            fixture.Options,
            fixture.Contract,
            "scanner",
            "verifier",
            TestContext.Current.CancellationToken
        );

        // Assert
        Assert.True(result.IsFailure);
        Assert.Equal(
            submitted,
            fixture.Commands.Any(command =>
                command.Contains("sonarscanner end", StringComparison.Ordinal)
            )
        );
        Assert.False(Directory.Exists(fixture.Scanner));
        Assert.False(File.Exists(Path.Combine(fixture.Output, "publish/readiness.json")));
        Assert.False(File.Exists(Path.Combine(fixture.Root, ".sonar-analysis.lock")));
    }

    [Fact]
    public async Task Run_TrustedScan_BindsRevisionAndWorkflowAttemptInScannerArguments()
    {
        // Arrange
        using var fixture = new RunnerFixture();
        var trust = new BoundaryTrust(0);

        // Act
        var result = await new AnalysisRunner(fixture, fixture, trust).RunAsync(
            fixture.Options,
            fixture.Contract,
            "scanner",
            "verifier",
            TestContext.Current.CancellationToken
        );

        // Assert
        Assert.True(result.IsSuccess, result.Error);
        var begin = Assert.Single(
            fixture.Commands,
            command => command.Contains("sonarscanner begin", StringComparison.Ordinal)
        );
        Assert.Contains("/d:sonar.scm.disabled=false", begin);
        Assert.Contains("/d:sonar.scm.revision=" + MainFixture.Sha, begin);
        Assert.Contains("/d:sonar.buildString=trusted-attempt", begin);
        Assert.Equal(4, trust.Checks);
    }

    private sealed class BoundaryTrust(int failAt) : IAnalysisTrust
    {
        internal int Checks { get; private set; }
        public string Revision => MainFixture.Sha;
        public string BuildString => "trusted-attempt";

        public Result ValidateTarget(AnalysisOptions options) => options.ValidateTarget();

        public Task<Result> CheckAsync(CancellationToken cancellationToken) =>
            Task.FromResult(++Checks == failAt ? Result.Fail("Boundary failed.") : Result.Ok());

        public Task<Result> VerifyAsync(
            AnalysisSummary summary,
            JsonElement latest,
            CancellationToken cancellationToken
        ) => CheckAsync(cancellationToken);
    }
}
