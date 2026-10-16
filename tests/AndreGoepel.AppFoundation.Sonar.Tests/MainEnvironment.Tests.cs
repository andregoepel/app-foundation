using AndreGoepel.AppFoundation.Sonar;
using Xunit;

namespace AndreGoepel.AppFoundation.Sonar.Tests;

public sealed class MainEnvironmentTests
{
    [Fact]
    public void Validate_TrustedMainContext_BindsTriggerAndCurrentAttempt()
    {
        // Arrange
        var fixture = new MainFixture();
        var root = Path.GetTempPath();
        var environment = MainFixture.Environment(root);

        // Act
        var result = MainEnvironment.Validate(
            root,
            name => environment.GetValueOrDefault(name),
            fixture.Payload()
        );

        // Assert
        Assert.True(result.IsSuccess, result.Error);
        Assert.Equal(fixture.Identity, result.Value);
    }

    [Theory]
    [InlineData("GITHUB_ACTIONS", "false")]
    [InlineData("GITHUB_REPOSITORY", "fork/app-foundation")]
    [InlineData("GITHUB_EVENT_NAME", "pull_request")]
    [InlineData("GITHUB_EVENT_NAME", "workflow_dispatch")]
    [InlineData("GITHUB_REF", "refs/heads/feature/unsafe")]
    [InlineData("GITHUB_WORKFLOW_REF", "unknown")]
    [InlineData("GITHUB_WORKFLOW_SHA", "other")]
    [InlineData("GITHUB_SHA", "malformed")]
    [InlineData("GITHUB_WORKSPACE", "other")]
    [InlineData("GITHUB_RUN_ID", "0")]
    [InlineData("GITHUB_RUN_ATTEMPT", "invalid")]
    public void Validate_UntrustedContext_RejectsBeforeCredentials(string key, string value)
    {
        // Arrange
        var fixture = new MainFixture();
        var root = Path.GetTempPath();
        var environment = MainFixture.Environment(root);
        environment[key] = value;

        // Act
        var result = MainEnvironment.Validate(
            root,
            name => environment.GetValueOrDefault(name),
            fixture.Payload()
        );

        // Assert
        Assert.True(result.IsFailure);
    }

    [Theory]
    [InlineData("event", "pull_request")]
    [InlineData("head_branch", "main-from-fork")]
    [InlineData("head_sha", "other")]
    [InlineData("path", "other")]
    [InlineData("conclusion", "cancelled")]
    public void Validate_UntrustedTrigger_Rejects(string property, string value)
    {
        // Arrange
        var fixture = new MainFixture();
        var root = Path.GetTempPath();
        var environment = MainFixture.Environment(root);
        fixture.Responses["actions/runs/200"][property] = value;

        // Act
        var result = MainEnvironment.Validate(
            root,
            name => environment.GetValueOrDefault(name),
            fixture.Payload()
        );

        // Assert
        Assert.True(result.IsFailure);
    }
}
