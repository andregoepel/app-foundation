using AndreGoepel.AppFoundation.Sonar;
using Xunit;

namespace AndreGoepel.AppFoundation.Sonar.Tests;

public sealed class NuGetIntentContextTests
{
    [Fact]
    public void Read_CurrentProductionContext_PinsRunAndBothDigests()
    {
        // Arrange
        using var fixture = new RunnerFixture();
        var options = Options(fixture.Root);
        var environment = EnvironmentValues();

        // Act
        var result = NuGetIntentContext.Read(options, name => environment.GetValueOrDefault(name));

        // Assert
        Assert.True(result.IsSuccess, result.Error);
        Assert.Equal(600, result.Value!.RunId);
        Assert.Equal(700, result.Value.ArtifactId);
        Assert.Equal("sha256:" + new string('d', 64), result.Value.ArchiveDigest);
        Assert.Equal(new string('e', 64), result.Value.ContentHash);
    }

    [Theory]
    [InlineData("GITHUB_REPOSITORY", "fork/app-foundation")]
    [InlineData("GITHUB_EVENT_NAME", "workflow_dispatch")]
    [InlineData("GITHUB_REF", "refs/tags/v2.2.0")]
    [InlineData("GITHUB_RUN_ATTEMPT", "2")]
    [InlineData("GITHUB_SHA", "other")]
    [InlineData("GITHUB_WORKFLOW_SHA", "other")]
    [InlineData("GITHUB_WORKFLOW_REF", "other")]
    [InlineData("GITHUB_RUN_ID", null)]
    [InlineData("GITHUB_RUN_ID", "0")]
    [InlineData("GITHUB_RUN_ID", "+600")]
    [InlineData("INTENT_ARTIFACT_ID", "0")]
    [InlineData("INTENT_ARTIFACT_ID", "-700")]
    [InlineData("INTENT_ARTIFACT_ID", "9223372036854775808")]
    [InlineData("INTENT_ARTIFACT_DIGEST", null)]
    [InlineData("INTENT_ARTIFACT_DIGEST", "wrong")]
    [InlineData("INTENT_SHA256", null)]
    [InlineData("INTENT_SHA256", "wrong")]
    public void Read_InvalidContext_RejectsBeforeNetwork(string name, string? value)
    {
        // Arrange
        using var fixture = new RunnerFixture();
        var environment = EnvironmentValues();
        environment[name] = value;

        // Act
        var result = NuGetIntentContext.Read(
            Options(fixture.Root),
            key => environment.GetValueOrDefault(key)
        );

        // Assert
        Assert.True(result.IsFailure);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void Read_AcceptanceTagOrPreparationRerun_Rejects(bool rerun)
    {
        // Arrange
        using var fixture = new RunnerFixture();
        var options = Options(fixture.Root);
        options = rerun
            ? options with
            {
                PreparationAttempt = 2,
            }
            : options with
            {
                Input = options.Input with
                {
                    Release = options.Input.Release with
                    {
                        Tag = "sonar-poc/nuget/0123456789abcdef0123456789abcdef/v2.2.0",
                    },
                },
            };
        var environment = EnvironmentValues();

        // Act
        var result = NuGetIntentContext.Read(options, key => environment.GetValueOrDefault(key));

        // Assert
        Assert.True(result.IsFailure);
    }

    private static NuGetHandoffOptions Options(string root) =>
        NuGetHandoffOptions
            .Parse(NuGetHandoffOptionsTests.Arguments(root, "--nuget-verify-intent"))
            .Value!;

    internal static Dictionary<string, string?> EnvironmentValues() =>
        new()
        {
            ["GITHUB_REPOSITORY"] = MainPolicy.Repository,
            ["GITHUB_EVENT_NAME"] = "workflow_run",
            ["GITHUB_REF"] = "refs/heads/main",
            ["GITHUB_RUN_ATTEMPT"] = "1",
            ["GITHUB_SHA"] = MainFixture.Sha,
            ["GITHUB_WORKFLOW_SHA"] = MainFixture.Sha,
            ["GITHUB_WORKFLOW_REF"] = NuGetIntentContext.WorkflowRef,
            ["GITHUB_RUN_ID"] = "600",
            ["INTENT_ARTIFACT_ID"] = "700",
            ["INTENT_ARTIFACT_DIGEST"] = new string('d', 64),
            ["INTENT_SHA256"] = new string('e', 64),
        };
}
