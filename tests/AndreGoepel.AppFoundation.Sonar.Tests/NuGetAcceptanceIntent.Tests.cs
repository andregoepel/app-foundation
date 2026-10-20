using AndreGoepel.AppFoundation.Sonar;
using Xunit;

namespace AndreGoepel.AppFoundation.Sonar.Tests;

public sealed class NuGetAcceptanceIntentTests
{
    internal const string Tag = "sonar-poc/nuget/0123456789abcdef0123456789abcdef/v2.2.0";

    [Theory]
    [InlineData(true, true, true, true)]
    [InlineData(true, true, false, false)]
    [InlineData(true, false, true, false)]
    [InlineData(false, true, true, false)]
    [InlineData(false, false, false, true)]
    public void Read_ModeTagAndWorkflow_MustBelongToSameBoundary(
        bool acceptanceMode,
        bool acceptanceTag,
        bool acceptanceWorkflow,
        bool expected
    )
    {
        // Arrange
        using var files = new RunnerFixture();
        var options = Options(files.Root) with { AcceptanceOnly = acceptanceMode };
        if (!acceptanceTag)
            options = options with
            {
                Input = options.Input with
                {
                    Release = options.Input.Release with { Tag = "v2.2.0" },
                },
            };
        var environment = NuGetIntentContextTests.EnvironmentValues();
        environment["GITHUB_WORKFLOW_REF"] = acceptanceWorkflow
            ? NuGetIntentContext.AcceptanceWorkflowRef
            : NuGetIntentContext.WorkflowRef;

        // Act
        var result = NuGetIntentContext.Read(options, environment.GetValueOrDefault);

        // Assert
        Assert.Equal(expected, result.IsSuccess);
        if (expected)
            Assert.Equal(acceptanceMode, result.Value!.AcceptanceOnly);
    }

    [Theory]
    [InlineData("valid")]
    [InlineData("production-path")]
    [InlineData("production-kind")]
    [InlineData("production-pins")]
    [InlineData("changed-digest")]
    [InlineData("changed-tag")]
    [InlineData("rerun")]
    public async Task Verify_AcceptanceIntent_AuthenticatesActualBoundaryWithoutPublication(
        string scenario
    )
    {
        // Arrange
        using var files = new RunnerFixture();
        using var fixture = new NuGetStoredIntentTests.IntentFixture(Options(files.Root));
        switch (scenario)
        {
            case "production-path":
                fixture.Run["path"] = NuGetIntentContext.WorkflowPath;
                break;
            case "production-kind":
                fixture.Intent["kind"] = "publication-intent";
                fixture.Seal();
                break;
            case "production-pins":
                fixture.Pins = fixture.Pins with { AcceptanceOnly = false };
                break;
            case "changed-digest":
                fixture.Pins = fixture.Pins with { ContentHash = new string('f', 64) };
                break;
            case "changed-tag":
                fixture.Intent["tag"] = "v2.2.0";
                fixture.Seal();
                break;
            case "rerun":
                fixture.Run["run_attempt"] = 2;
                break;
        }

        // Act
        var result = await fixture.VerifyAsync();

        // Assert
        Assert.Equal(scenario == "valid", result.IsSuccess);
        if (scenario == "valid")
        {
            Assert.Equal(1, fixture.Downloads);
            Assert.Equal(2, fixture.RunReads);
        }
    }

    [Theory]
    [InlineData("--nuget-verify-acceptance-intent", Tag, true)]
    [InlineData("--nuget-verify-acceptance-intent", "v2.2.0", false)]
    [InlineData("--nuget-verify-acceptance-intent", "sonar-poc/nuget/invalid/v2.2.0", false)]
    [InlineData("--nuget-inspect-acceptance", Tag, true)]
    [InlineData("--nuget-inspect-acceptance", "v2.2.0", false)]
    [InlineData("--nuget-inspect-publication", Tag, false)]
    public void Parse_AcceptanceMode_RequiresTestTag(string mode, string tag, bool expected)
    {
        // Arrange
        using var files = new RunnerFixture();
        var args = NuGetHandoffOptionsTests.Arguments(files.Root, mode);
        args[10] = tag;
        if (mode.StartsWith("--nuget-inspect-", StringComparison.Ordinal))
            args =
            [
                .. args,
                "--publish-run",
                "600",
                "--intent-artifact",
                "700",
                "--intent-archive-sha256",
                new string('d', 64),
                "--intent-sha256",
                new string('e', 64),
            ];

        // Act
        var result = NuGetHandoffOptions.Parse(args);

        // Assert
        Assert.Equal(expected, result.IsSuccess);
        if (expected)
        {
            Assert.True(result.Value!.AcceptanceOnly);
            Assert.True(result.Value.VerifyOnly);
            Assert.Null(result.Value.Files);
        }
    }

    internal static NuGetHandoffOptions Options(string root)
    {
        var args = NuGetHandoffOptionsTests.Arguments(root, "--nuget-verify-acceptance-intent");
        args[10] = Tag;
        return NuGetHandoffOptions.Parse(args).Value!;
    }

    [Theory]
    [InlineData("success", true)]
    [InlineData("missing", true)]
    [InlineData("cancelled", true)]
    [InlineData("production-kind", false)]
    [InlineData("production-workflow", false)]
    public async Task Inspect_AcceptanceRecords_NeverReportPublicationAuthority(
        string scenario,
        bool expected
    )
    {
        // Arrange
        using var files = new RunnerFixture();
        using var fixture = new NuGetPublicationRecordsTests.RecordFixture(Options(files.Root));
        if (scenario == "production-kind")
            fixture.Outcome["kind"] = "publication-outcomes";
        else if (scenario == "production-workflow")
            fixture.Outcome["workflowRef"] = NuGetIntentContext.WorkflowRef;
        else
            fixture.SetScenario(scenario);
        if (scenario != "missing")
            fixture.Seal();

        // Act
        var result = await fixture.InspectAsync();

        // Assert
        Assert.Equal(expected, result.IsSuccess);
        if (expected)
        {
            Assert.True(result.Value!.AcceptanceOnly);
            Assert.False(result.Value.ReleaseEligible);
            Assert.False(result.Value.AuthorizesRetry);
            Assert.False(result.Value.RemoteAcceptanceVerified);
            Assert.Equal(12, result.Value.Packages.Length);
        }
    }
}
