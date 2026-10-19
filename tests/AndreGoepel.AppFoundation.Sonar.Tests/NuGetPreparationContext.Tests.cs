using System.Text.Json;
using System.Text.Json.Nodes;
using AndreGoepel.AppFoundation.Sonar;
using Xunit;

namespace AndreGoepel.AppFoundation.Sonar.Tests;

public sealed class NuGetPreparationContextTests
{
    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void Capture_TagCreation_PreservesEventObjectWithoutReadingLiveRef(bool annotated)
    {
        // Arrange
        var release = Release();
        var environment = EnvironmentFor(release);
        var payload = Payload(annotated);

        // Act
        var result = NuGetPreparationContext.Capture(
            release,
            environment.GetValueOrDefault,
            Element(payload)
        );

        // Assert
        Assert.True(result.IsSuccess, result.Error);
        Assert.Equal(
            annotated ? NuGetHandoffTests.HandoffFixture.TagObject : release.Sha,
            result.Value!.TagObjectSha
        );
        Assert.Equal(500, result.Value.RunId);
        Assert.Equal(1, result.Value.Attempt);
        payload["after"] = new string('b', 40);
        environment["GITHUB_RUN_ATTEMPT"] = "2";
        Assert.Equal(1, result.Value.Attempt);
        Assert.NotEqual(payload["after"]!.GetValue<string>(), result.Value.TagObjectSha);
    }

    [Theory]
    [InlineData("GITHUB_ACTIONS", "false")]
    [InlineData("GITHUB_REPOSITORY", "fork/app-foundation")]
    [InlineData("GITHUB_EVENT_NAME", "workflow_dispatch")]
    [InlineData("GITHUB_REF", "refs/heads/v2.2.0")]
    [InlineData("GITHUB_REF_TYPE", "branch")]
    [InlineData("GITHUB_SHA", "wrong")]
    [InlineData("GITHUB_WORKFLOW_SHA", "wrong")]
    [InlineData("GITHUB_WORKFLOW_REF", "unreviewed")]
    [InlineData("GITHUB_WORKSPACE", "elsewhere")]
    [InlineData("GITHUB_RUN_ID", "0")]
    [InlineData("GITHUB_RUN_ID", "invalid")]
    [InlineData("GITHUB_RUN_ATTEMPT", "0")]
    [InlineData("GITHUB_RUN_ATTEMPT", "invalid")]
    public void Capture_UntrustedContext_Rejects(string name, string value)
    {
        // Arrange
        var release = Release();
        var environment = EnvironmentFor(release);
        environment[name] = value;

        // Act
        var result = NuGetPreparationContext.Capture(
            release,
            environment.GetValueOrDefault,
            Element(Payload(false))
        );

        // Assert
        Assert.True(result.IsFailure);
    }

    [Theory]
    [InlineData("ref", "\"refs/heads/v2.2.0\"")]
    [InlineData("created", "false")]
    [InlineData("deleted", "true")]
    [InlineData("forced", "true")]
    [InlineData("before", "\"1111111111111111111111111111111111111111\"")]
    [InlineData("after", "null")]
    [InlineData("after", "\"invalid\"")]
    [InlineData("after", "\"0000000000000000000000000000000000000000\"")]
    [InlineData("repository", "{\"full_name\":\"fork/app-foundation\"}")]
    public void Capture_ChangedOrUnrelatedEvent_Rejects(string property, string json)
    {
        // Arrange
        var release = Release();
        var environment = EnvironmentFor(release);
        var payload = Payload(false);
        payload[property] = JsonNode.Parse(json);

        // Act
        var result = NuGetPreparationContext.Capture(
            release,
            environment.GetValueOrDefault,
            Element(payload)
        );

        // Assert
        Assert.True(result.IsFailure);
    }

    [Theory]
    [InlineData("missing")]
    [InlineData("relative")]
    [InlineData("absent")]
    [InlineData("oversized")]
    public async Task Read_MissingOrInvalidEvent_DoesNotCapture(string fault)
    {
        // Arrange
        using var fixture = new RunnerFixture();
        var release = Release() with { Root = fixture.Root };
        var environment = EnvironmentFor(release);
        var path = Path.Combine(fixture.Root, "event.json");
        if (fault != "missing")
            environment["GITHUB_EVENT_PATH"] = fault == "relative" ? "event.json" : path;
        if (fault == "oversized")
            await File.WriteAllBytesAsync(
                path,
                new byte[1048577],
                TestContext.Current.CancellationToken
            );

        // Act
        var result = await NuGetPreparationContext.ReadAsync(
            release,
            environment.GetValueOrDefault,
            TestContext.Current.CancellationToken
        );

        // Assert
        Assert.True(result.IsFailure);
    }

    [Fact]
    public async Task Read_OriginalEvent_CapturesBeforeFileChanges()
    {
        // Arrange
        using var fixture = new RunnerFixture();
        var release = Release() with { Root = fixture.Root };
        var environment = EnvironmentFor(release);
        var path = Path.Combine(fixture.Root, "event.json");
        environment["GITHUB_EVENT_PATH"] = path;
        await File.WriteAllTextAsync(
            path,
            Payload(false).ToJsonString(),
            TestContext.Current.CancellationToken
        );

        // Act
        var result = await NuGetPreparationContext.ReadAsync(
            release,
            environment.GetValueOrDefault,
            TestContext.Current.CancellationToken
        );
        await File.WriteAllTextAsync(path, "{}", TestContext.Current.CancellationToken);

        // Assert
        Assert.True(result.IsSuccess, result.Error);
        Assert.Equal(release.Sha, result.Value!.TagObjectSha);
    }

    internal static NuGetRelease Release() =>
        new(Path.GetFullPath(Path.GetTempPath()), MainFixture.Sha, "2.2.0", 100, "v2.2.0");

    internal static Dictionary<string, string?> EnvironmentFor(NuGetRelease release) =>
        new()
        {
            ["GITHUB_ACTIONS"] = "true",
            ["GITHUB_REPOSITORY"] = MainPolicy.Repository,
            ["GITHUB_EVENT_NAME"] = "push",
            ["GITHUB_REF"] = "refs/tags/" + release.Tag,
            ["GITHUB_REF_TYPE"] = "tag",
            ["GITHUB_SHA"] = release.Sha,
            ["GITHUB_WORKFLOW_SHA"] = release.Sha,
            ["GITHUB_WORKFLOW_REF"] =
                $"{MainPolicy.Repository}/{NuGetPreparationPolicy.WorkflowPath}@refs/tags/{release.Tag}",
            ["GITHUB_WORKSPACE"] = release.Root,
            ["GITHUB_RUN_ID"] = "500",
            ["GITHUB_RUN_ATTEMPT"] = "1",
        };

    internal static JsonNode Payload(bool annotated, string tag = "v2.2.0") =>
        MainFixture.Node(
            new
            {
                repository = new { full_name = MainPolicy.Repository },
                @ref = "refs/tags/" + tag,
                created = true,
                deleted = false,
                forced = false,
                before = new string('0', 40),
                after = annotated ? NuGetHandoffTests.HandoffFixture.TagObject : MainFixture.Sha,
            }
        );

    internal static JsonElement Element(JsonNode node) => JsonSerializer.SerializeToElement(node);
}
