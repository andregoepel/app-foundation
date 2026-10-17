using AndreGoepel.AppFoundation.Sonar;
using Xunit;

namespace AndreGoepel.AppFoundation.Sonar.Tests;

public sealed class GitHubEvidenceTests
{
    [Fact]
    public async Task Read_CompleteTrustedRuns_BindsJobsAndAttempts()
    {
        // Arrange
        var fixture = new MainFixture();

        // Act
        var result = await fixture.GitHub().ReadAsync(TestContext.Current.CancellationToken);

        // Assert
        Assert.True(result.IsSuccess, result.Error);
        Assert.Equal([200L, 300L], result.Value!.Select(r => r.Id));
        Assert.All(result.Value!, r => Assert.Equal(MainFixture.Sha, r.Sha));
        Assert.Equal(3, result.Value!.Sum(r => r.Jobs.Length));
    }

    [Theory]
    [InlineData("head_sha", "wrong")]
    [InlineData("head_branch", "feature/unsafe")]
    [InlineData("event", "pull_request")]
    [InlineData("path", ".github/workflows/unknown.yml")]
    [InlineData("conclusion", "failure")]
    [InlineData("conclusion", "cancelled")]
    [InlineData("conclusion", "skipped")]
    [InlineData("run_started_at", "2000-01-01T00:00:00Z")]
    [InlineData("run_started_at", "2099-01-01T00:00:00Z")]
    public async Task Read_InvalidCiRun_Rejects(string property, string value)
    {
        // Arrange
        var fixture = new MainFixture();
        fixture.Responses["actions/runs/200"][property] = value;

        // Act
        var result = await fixture.GitHub().ReadAsync(TestContext.Current.CancellationToken);

        // Assert
        Assert.True(result.IsFailure);
    }

    [Theory]
    [InlineData("main")]
    [InlineData("repository")]
    [InlineData("head-repository")]
    [InlineData("workflow-id")]
    [InlineData("ci-attempt")]
    [InlineData("shared-sha")]
    [InlineData("shared-path")]
    [InlineData("own-attempt")]
    [InlineData("own-cancelled")]
    [InlineData("inventory")]
    public async Task Read_ChangedIdentityOrIncompleteInventory_Rejects(string change)
    {
        // Arrange
        var fixture = new MainFixture();
        var run = fixture.Responses["actions/runs/200"];
        switch (change)
        {
            case "main":
                fixture.Responses["git/ref/heads/main"]["object"]!["sha"] = "newer";
                break;
            case "repository":
                run["repository"]!["full_name"] = "other/repo";
                break;
            case "head-repository":
                run["head_repository"]!["full_name"] = "other/repo";
                break;
            case "workflow-id":
                run["workflow_id"] = 99;
                break;
            case "ci-attempt":
                run["run_attempt"] = 2;
                break;
            case "shared-sha":
                run["referenced_workflows"]![0]!["sha"] = "changed";
                break;
            case "shared-path":
                run["referenced_workflows"]![0]!["path"] = "unknown";
                break;
            case "own-attempt":
                fixture.Responses["actions/runs/100"]["run_attempt"] = 2;
                break;
            case "own-cancelled":
                fixture.Responses["actions/runs/100"]["status"] = "completed";
                break;
            case "inventory":
                fixture
                    .Responses.First(p =>
                        p.Key.StartsWith("actions/workflows/", StringComparison.Ordinal)
                    )
                    .Value["total_count"] = 101;
                break;
        }

        // Act
        var result = await fixture.GitHub().ReadAsync(TestContext.Current.CancellationToken);

        // Assert
        Assert.True(result.IsFailure);
    }

    [Theory]
    [InlineData("failure")]
    [InlineData("cancelled")]
    [InlineData("skipped")]
    [InlineData("missing")]
    [InlineData("duplicate")]
    [InlineData("pending")]
    [InlineData("incomplete")]
    public async Task Read_UnsuccessfulOrMissingE2eJob_Rejects(string state)
    {
        // Arrange
        var fixture = new MainFixture();
        var document = fixture.Responses["actions/runs/300/attempts/1/jobs?per_page=100"];
        var jobs = document["jobs"]!.AsArray();
        if (state == "missing")
        {
            jobs.Clear();
            document["total_count"] = 0;
        }
        else if (state == "duplicate")
        {
            jobs.Add(jobs[0]!.DeepClone());
            document["total_count"] = 2;
        }
        else if (state == "pending")
            jobs[0]!["status"] = "in_progress";
        else if (state == "incomplete")
            document["total_count"] = 101;
        else
            jobs[0]!["conclusion"] = state;

        // Act
        var result = await fixture.GitHub().ReadAsync(TestContext.Current.CancellationToken);

        // Assert
        Assert.True(result.IsFailure);
    }

    [Theory]
    [InlineData(0)]
    [InlineData(1)]
    [InlineData(2)]
    public async Task Read_FailedBuildTestOrAuditStep_RejectsSuccessfulJob(int step)
    {
        // Arrange
        var fixture = new MainFixture();
        fixture.Responses["actions/runs/200/attempts/1/jobs?per_page=100"]["jobs"]![0]!["steps"]![
            step
        ]!["conclusion"] = "failure";

        // Act
        var result = await fixture.GitHub().ReadAsync(TestContext.Current.CancellationToken);

        // Assert
        Assert.True(result.IsFailure);
    }

    [Fact]
    public async Task Wait_PendingE2e_WaitsForItsSuccessfulAttempt()
    {
        // Arrange
        var fixture = new MainFixture();
        fixture.Responses["actions/runs/300"]["status"] = "in_progress";
        fixture.OnDelay = () => fixture.Responses["actions/runs/300"]["status"] = "completed";

        // Act
        var result = await fixture.GitHub().WaitAsync(TestContext.Current.CancellationToken, 2);

        // Assert
        Assert.True(result.IsSuccess, result.Error);
        Assert.Equal(1, fixture.Delays);
        Assert.Equal(2, result.Value!.Length);
    }

    [Fact]
    public async Task Wait_MissingE2e_StopsAtDeadline()
    {
        // Arrange
        var fixture = new MainFixture();
        var inventory = fixture
            .Responses.First(p =>
                p.Key.StartsWith("actions/workflows/20/", StringComparison.Ordinal)
            )
            .Value;
        inventory["workflow_runs"]!.AsArray().Clear();
        inventory["total_count"] = 0;

        // Act
        var result = await fixture.GitHub().WaitAsync(TestContext.Current.CancellationToken, 2);

        // Assert
        Assert.True(result.IsFailure);
        Assert.Equal(2, fixture.Delays);
    }

    [Fact]
    public async Task Read_ApiOutage_DoesNotReturnEvidence()
    {
        // Arrange
        var fixture = new MainFixture { Failure = new HttpRequestException("unavailable") };

        // Act / Assert
        await Assert.ThrowsAsync<HttpRequestException>(() =>
            fixture.GitHub().ReadAsync(TestContext.Current.CancellationToken)
        );
    }

    [Fact]
    public async Task Read_FreshRerunOfOlderWorkflow_UsesCurrentAttemptAge()
    {
        // Arrange
        var fixture = new MainFixture();
        fixture.Identity = fixture.Identity with { TriggerAttempt = 2 };
        fixture.Responses["actions/runs/200"]["created_at"] = "2000-01-01T00:00:00Z";
        fixture.Responses["actions/runs/200"]["run_attempt"] = 2;
        fixture.Responses["actions/runs/200/attempts/2/jobs?per_page=100"] = fixture
            .Responses["actions/runs/200/attempts/1/jobs?per_page=100"]
            .DeepClone();

        // Act
        var result = await fixture.GitHub().ReadAsync(TestContext.Current.CancellationToken);

        // Assert
        Assert.True(result.IsSuccess, result.Error);
        Assert.Equal(2, result.Value![0].Attempt);
        Assert.Equal(fixture.Now.AddMinutes(-5), result.Value![0].StartedAt);
    }

    [Fact]
    public async Task Read_OldJobReusedInFreshAttempt_Rejects()
    {
        // Arrange
        var fixture = new MainFixture();
        fixture.Responses["actions/runs/200/attempts/1/jobs?per_page=100"]["jobs"]![0]![
            "started_at"
        ] = "2000-01-01T00:00:00Z";

        // Act
        var result = await fixture.GitHub().ReadAsync(TestContext.Current.CancellationToken);

        // Assert
        Assert.True(result.IsFailure);
    }
}
