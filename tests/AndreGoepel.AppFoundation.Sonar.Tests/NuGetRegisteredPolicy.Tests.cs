using System.Text;
using System.Text.Json.Nodes;
using AndreGoepel.AppFoundation.Sonar;
using Xunit;

namespace AndreGoepel.AppFoundation.Sonar.Tests;

public sealed class NuGetRegisteredPolicyTests
{
    [Fact]
    public async Task Run_RegisteredPolicyAndSyntheticSuccess_VerifiesAllTwelveFiles()
    {
        // Arrange
        using var fixture = await CreateFixtureAsync(TestContext.Current.CancellationToken);

        // Act
        var result = await fixture.RunAsync(TestContext.Current.CancellationToken);

        // Assert
        Assert.True(result.IsSuccess, result.Error);
        Assert.Equal(12, fixture.Sink.Accepted.Count);
        Assert.True(result.Value!.DryRun);
        Assert.False(result.Value.ReleaseEligible);
    }

    [Theory]
    [InlineData("skipped")]
    [InlineData("cancelled")]
    [InlineData("failure")]
    public async Task Run_UnsuccessfulRegisteredJob_BlocksBeforeDownloadOrLogin(string conclusion)
    {
        // Arrange
        using var fixture = await CreateFixtureAsync(TestContext.Current.CancellationToken);
        var job = Job(fixture);
        job["conclusion"] = conclusion;
        job["started_at"] = null;
        job["steps"] = new JsonArray();

        // Act
        var result = await fixture.RunAsync(TestContext.Current.CancellationToken);

        // Assert
        Assert.True(result.IsFailure);
        Assert.Contains("A mandatory job", result.Error);
        Assert.Equal(0, fixture.Downloads);
        Assert.Empty(fixture.Sink.Events);
    }

    [Theory]
    [InlineData("Verify tag push", false)]
    [InlineData("Verify tag push", true)]
    [InlineData("Prepare NuGet batch", false)]
    [InlineData("Prepare NuGet batch", true)]
    [InlineData("Publish package batch", false)]
    [InlineData("Publish package batch", true)]
    [InlineData("Publish origin", false)]
    [InlineData("Publish origin", true)]
    public async Task Run_MissingOrSkippedRegisteredStep_BlocksBeforeDownloadOrLogin(
        string name,
        bool missing
    )
    {
        // Arrange
        using var fixture = await CreateFixtureAsync(TestContext.Current.CancellationToken);
        var steps = (JsonArray)Job(fixture)["steps"]!;
        var step = steps.Single(step => step!["name"]!.GetValue<string>() == name)!;
        if (missing)
            steps.Remove(step);
        else
            step["conclusion"] = "skipped";

        // Act
        var result = await fixture.RunAsync(TestContext.Current.CancellationToken);

        // Assert
        Assert.True(result.IsFailure, name);
        Assert.Contains("A mandatory build, test, audit or drift step", result.Error);
        Assert.Equal(0, fixture.Downloads);
        Assert.Empty(fixture.Sink.Events);
    }

    [Fact]
    public async Task Run_ChangingProducerTriggerWithoutPolicyReview_BlocksBeforeDownloadOrLogin()
    {
        // Arrange
        using var fixture = await CreateFixtureAsync(TestContext.Current.CancellationToken);
        var response = fixture.Handoff.Evidence.Main.Responses[
            $"contents/{NuGetPreparationPolicy.WorkflowPath}?ref={MainFixture.Sha}"
        ];
        var workflow = Encoding.UTF8.GetString(
            Convert.FromBase64String(response["content"]!.GetValue<string>())
        );
        var changedWorkflow = workflow.Replace("tags: [\"v*\"]", "tags: [\"*\"]");
        Assert.NotEqual(workflow, changedWorkflow);
        response["content"] = Convert.ToBase64String(Encoding.UTF8.GetBytes(changedWorkflow));

        // Act
        var result = await fixture.RunAsync(TestContext.Current.CancellationToken);

        // Assert
        Assert.True(result.IsFailure);
        Assert.Equal("The preparation workflow revision is not approved.", result.Error);
        Assert.Equal(0, fixture.Downloads);
        Assert.Empty(fixture.Sink.Events);
    }

    private static JsonNode Job(NuGetProvenanceTests.ProvenanceFixture fixture) =>
        fixture.Handoff.Evidence.Main.Responses["actions/runs/500/attempts/1/jobs?per_page=100"][
            "jobs"
        ]![0]!;

    private static async Task<NuGetProvenanceTests.ProvenanceFixture> CreateFixtureAsync(
        CancellationToken ct
    )
    {
        var root = NuGetCommandTests.RepositoryRoot();
        var policy = await NuGetPreparationPolicy.ReadAsync(root, ct);
        var workflow = await File.ReadAllBytesAsync(
            Path.Combine(root, NuGetPreparationPolicy.WorkflowPath),
            ct
        );
        var fixture = new NuGetProvenanceTests.ProvenanceFixture(policy.Value!, workflow, "v2.2.0");
        await fixture.InitializeAsync(ct);
        return fixture;
    }
}
