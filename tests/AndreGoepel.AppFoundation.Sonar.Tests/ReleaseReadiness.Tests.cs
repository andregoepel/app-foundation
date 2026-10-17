using System.Text.Json.Nodes;
using AndreGoepel.AppFoundation.Sonar;
using Xunit;

namespace AndreGoepel.AppFoundation.Sonar.Tests;

public sealed class ReleaseReadinessTests
{
    [Fact]
    public async Task Verify_CompleteCurrentEvidence_PermitsPreparation()
    {
        // Arrange
        using var fixture = new ReleaseFixture();
        await fixture.InitializeAsync(TestContext.Current.CancellationToken);

        // Act
        var result = await fixture
            .Verifier()
            .VerifyAsync(fixture.Release, TestContext.Current.CancellationToken);

        // Assert
        Assert.True(result.IsSuccess, result.Error);
        Assert.Equal(["readiness.json", "summary.json"], fixture.Downloads);
        Assert.True(fixture.Main.Requests.Count(p => p == "actions/runs/100") >= 4);
    }

    [Theory]
    [InlineData("head_sha", "wrong-sha")]
    [InlineData("head_branch", "feature/unmerged")]
    [InlineData("path", ".github/workflows/unknown.yml")]
    [InlineData("event", "pull_request")]
    [InlineData("status", "in_progress")]
    [InlineData("conclusion", "failure")]
    [InlineData("conclusion", "skipped")]
    [InlineData("conclusion", "cancelled")]
    public async Task Verify_UntrustedProducer_RejectsBeforeArtifactDownload(
        string field,
        string value
    )
    {
        // Arrange
        using var fixture = new ReleaseFixture();
        await fixture.InitializeAsync(TestContext.Current.CancellationToken);
        fixture.Producer[field] = value;

        // Act
        var result = await fixture
            .Verifier()
            .VerifyAsync(fixture.Release, TestContext.Current.CancellationToken);

        // Assert
        Assert.True(result.IsFailure);
        Assert.Empty(fixture.Downloads);
    }

    [Theory]
    [InlineData("wrong-main")]
    [InlineData("wrong-checkout")]
    [InlineData("dirty-checkout")]
    [InlineData("unknown-workflow-id")]
    [InlineData("new-producer")]
    [InlineData("incomplete-inventory")]
    [InlineData("foreign-repository")]
    [InlineData("foreign-head-repository")]
    public async Task Verify_WrongSourceOrWorkflow_Blocks(string fault)
    {
        // Arrange
        using var fixture = new ReleaseFixture();
        await fixture.InitializeAsync(TestContext.Current.CancellationToken);
        switch (fault)
        {
            case "wrong-main":
                fixture.Main.Responses["git/ref/heads/main"]["object"]!["sha"] = "unmerged";
                break;
            case "wrong-checkout":
                fixture.Head = "wrong";
                break;
            case "dirty-checkout":
                fixture.Dirty = true;
                break;
            case "unknown-workflow-id":
                fixture.Producer["workflow_id"] = 999;
                break;
            case "new-producer":
                fixture.Main.Responses[fixture.ProducerInventory]["workflow_runs"]![0]!["id"] = 101;
                break;
            case "incomplete-inventory":
                fixture.Main.Responses[fixture.ProducerInventory]["total_count"] = 2;
                break;
            case "foreign-repository":
                fixture.Producer["repository"]!["full_name"] = "someone/fork";
                break;
            case "foreign-head-repository":
                fixture.Producer["head_repository"]!["full_name"] = "someone/fork";
                break;
        }

        // Act
        var result = await fixture
            .Verifier()
            .VerifyAsync(fixture.Release, TestContext.Current.CancellationToken);

        // Assert
        Assert.True(result.IsFailure);
        Assert.Empty(fixture.Downloads);
    }

    [Theory]
    [InlineData("expired")]
    [InlineData("missing")]
    [InlineData("duplicate")]
    [InlineData("old-attempt")]
    [InlineData("wrong-source")]
    [InlineData("oversized")]
    public async Task Verify_UntrustedArtifact_Blocks(string fault)
    {
        // Arrange
        using var fixture = new ReleaseFixture();
        await fixture.InitializeAsync(TestContext.Current.CancellationToken);
        var items = (JsonArray)fixture.ArtifactInventory["artifacts"]!;
        switch (fault)
        {
            case "expired":
                items[0]!["expired"] = true;
                break;
            case "missing":
                items.RemoveAt(0);
                fixture.ArtifactInventory["total_count"] = 1;
                break;
            case "duplicate":
                items.Add(items[0]!.DeepClone());
                fixture.ArtifactInventory["total_count"] = 3;
                break;
            case "old-attempt":
                fixture.Producer["run_attempt"] = 2;
                break;
            case "wrong-source":
                items[0]!["workflow_run"]!["head_sha"] = "wrong";
                break;
            case "oversized":
                items[0]!["size_in_bytes"] = GitHubArtifacts.MaximumBytes + 1;
                break;
        }

        // Act
        var result = await fixture
            .Verifier()
            .VerifyAsync(fixture.Release, TestContext.Current.CancellationToken);

        // Assert
        Assert.True(result.IsFailure);
        Assert.Empty(fixture.Downloads);
    }

    [Theory]
    [InlineData("policySha256")]
    [InlineData("analysisContractSha256")]
    [InlineData("analysisReceiptSha256")]
    [InlineData("sourceSha256")]
    [InlineData("workflowRevision")]
    [InlineData("baselineAnalysisId")]
    [InlineData("taskId")]
    [InlineData("analysisId")]
    [InlineData("workflowPath")]
    [InlineData("repository")]
    [InlineData("policyVersion")]
    public async Task Verify_ChangedReceiptBinding_Blocks(string field)
    {
        // Arrange
        using var fixture = new ReleaseFixture();
        await fixture.InitializeAsync(TestContext.Current.CancellationToken);
        fixture.Receipt[field] = "changed";

        // Act
        var result = await fixture
            .Verifier()
            .VerifyAsync(fixture.Release, TestContext.Current.CancellationToken);

        // Assert
        Assert.True(result.IsFailure);
    }

    [Theory]
    [InlineData("expired")]
    [InlineData("future")]
    [InlineData("too-long")]
    [InlineData("false-eligibility")]
    [InlineData("unknown-schema")]
    [InlineData("wrong-attempt")]
    [InlineData("wrong-sha")]
    [InlineData("missing-e2e")]
    [InlineData("failed-test")]
    [InlineData("failed-gate")]
    [InlineData("failed-scanner")]
    [InlineData("late-analysis")]
    [InlineData("missing-project")]
    public async Task Verify_InvalidReadiness_Blocks(string fault)
    {
        // Arrange
        using var fixture = new ReleaseFixture();
        await fixture.InitializeAsync(TestContext.Current.CancellationToken);
        switch (fault)
        {
            case "missing-project":
                fixture.Summary["analyzedProjects"] = new JsonArray();
                break;
            case "expired":
                fixture.Receipt["expiresAt"] = fixture.Main.Now.AddMinutes(-1);
                break;
            case "future":
                fixture.Receipt["issuedAt"] = fixture.Main.Now.AddMinutes(1);
                break;
            case "too-long":
                fixture.Receipt["expiresAt"] = fixture.Main.Now.AddHours(2);
                break;
            case "false-eligibility":
                fixture.Receipt["releaseEligible"] = false;
                break;
            case "unknown-schema":
                fixture.Receipt["schemaVersion"] = 2;
                break;
            case "wrong-attempt":
                fixture.Receipt["run"]!["attempt"] = 2;
                break;
            case "wrong-sha":
                fixture.Receipt["run"]!["sha"] = "unmerged";
                break;
            case "missing-e2e":
                fixture.Summary["tests"] = new JsonObject();
                break;
            case "failed-test":
                fixture.Summary["tests"]!["Example.E2ETests"]!["passed"] = 0;
                break;
            case "failed-gate":
                fixture.Summary["gate"]!["status"] = "ERROR";
                break;
            case "failed-scanner":
                fixture.Summary["scannerExitCode"] = 1;
                break;
            case "late-analysis":
                fixture.Summary["finishedAt"] = fixture.Main.Now.AddHours(1);
                break;
        }
        fixture.RehashSummary();

        // Act
        var result = await fixture
            .Verifier()
            .VerifyAsync(fixture.Release, TestContext.Current.CancellationToken);

        // Assert
        Assert.True(result.IsFailure);
    }

    [Theory]
    [InlineData("missing-e2e")]
    [InlineData("skipped-step")]
    [InlineData("new-ci-attempt")]
    [InlineData("shared-workflow-change")]
    [InlineData("gate-error")]
    [InlineData("baseline-change")]
    [InlineData("policy-change")]
    [InlineData("superseded-analysis")]
    [InlineData("wrong-task")]
    [InlineData("wrong-server")]
    [InlineData("missing-denominator")]
    [InlineData("gate-reassigned")]
    public async Task Verify_ChangedLiveEvidence_Blocks(string fault)
    {
        // Arrange
        using var fixture = new ReleaseFixture();
        await fixture.InitializeAsync(TestContext.Current.CancellationToken);
        switch (fault)
        {
            case "gate-reassigned":
                fixture.Main.Responses["qualitygates/get_by_project"]["qualityGate"]!["name"] =
                    "Other gate";
                break;
            case "missing-e2e":
                fixture.Main.Responses[
                    $"actions/workflows/20/runs?branch=main&event=push&head_sha={MainFixture.Sha}&per_page=100"
                ] = MainFixture.Node(
                    new { total_count = 0, workflow_runs = Array.Empty<object>() }
                );
                break;
            case "skipped-step":
                fixture.Main.Responses["actions/runs/300/attempts/1/jobs?per_page=100"]["jobs"]![
                    0
                ]!["steps"]![0]!["conclusion"] = "skipped";
                break;
            case "new-ci-attempt":
                fixture.Main.Responses["actions/runs/200"]["run_attempt"] = 2;
                break;
            case "shared-workflow-change":
                fixture.Main.Responses["actions/runs/200"]["referenced_workflows"]![0]!["sha"] =
                    "wrong";
                break;
            case "gate-error":
                fixture.Main.Responses["qualitygates/project_status"]["projectStatus"]!["status"] =
                    "ERROR";
                break;
            case "baseline-change":
                fixture.Main.Responses["new_code_periods/show"]["value"] = "changed";
                break;
            case "policy-change":
                fixture.Main.Responses["qualitygates/show"]["conditions"]![0]!["error"] = "10";
                break;
            case "superseded-analysis":
                fixture.Main.Responses["project_analyses/search"]["analyses"]![0]!["key"] =
                    "new-analysis";
                break;
            case "wrong-task":
                fixture.Main.Responses["ce/task"]["task"]!["analysisId"] = "wrong";
                break;
            case "wrong-server":
                fixture.Main.Responses["system/status"]["id"] = "wrong";
                break;
            case "missing-denominator":
                var conditions = (JsonArray)
                    fixture.Main.Responses["qualitygates/project_status"]["projectStatus"]![
                        "conditions"
                    ]!;
                conditions.RemoveAt(3);
                fixture.Main.Responses["measures/component"]["component"]!["measures"] =
                    new JsonArray();
                fixture.Summary["measures"]!["new_lines_to_cover"] = "0";
                fixture.Summary["measures"]!["new_conditions_to_cover"] = "0";
                fixture.RehashSummary();
                break;
        }

        // Act
        var result = await fixture
            .Verifier()
            .VerifyAsync(fixture.Release, TestContext.Current.CancellationToken);

        // Assert
        Assert.True(result.IsFailure);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task Verify_CurrentTag_PeelsBothTagKinds(bool annotated)
    {
        // Arrange
        using var fixture = new ReleaseFixture();
        await fixture.InitializeAsync(TestContext.Current.CancellationToken);
        var tagObject = new
        {
            @object = new { type = annotated ? "tag" : "commit", sha = MainFixture.Sha },
        };
        fixture.Main.Responses["git/ref/tags/v2.2.0"] = MainFixture.Node(tagObject);
        fixture.Main.Responses["git/tags/" + MainFixture.Sha] = MainFixture.Node(
            new { @object = new { type = "commit", sha = MainFixture.Sha } }
        );

        // Act
        var result = await fixture
            .Verifier()
            .VerifyAsync(
                fixture.Release with
                {
                    Tag = "v2.2.0",
                },
                TestContext.Current.CancellationToken
            );

        // Assert
        Assert.True(result.IsSuccess, result.Error);
    }

    [Fact]
    public async Task Verify_TagChangesDuringDownload_Blocks()
    {
        // Arrange
        using var fixture = new ReleaseFixture();
        await fixture.InitializeAsync(TestContext.Current.CancellationToken);
        fixture.Main.Responses["git/ref/tags/v2.2.0"] = MainFixture.Node(
            new { @object = new { type = "commit", sha = MainFixture.Sha } }
        );
        fixture.OnReadArtifact = _ =>
            fixture.Main.Responses["git/ref/tags/v2.2.0"]["object"]!["sha"] = "changed";

        // Act
        var result = await fixture
            .Verifier()
            .VerifyAsync(
                fixture.Release with
                {
                    Tag = "v2.2.0",
                },
                TestContext.Current.CancellationToken
            );

        // Assert
        Assert.True(result.IsFailure);
    }
}
