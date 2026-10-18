using System.IO.Compression;
using System.Net;
using System.Text;
using System.Text.Json.Nodes;
using AndreGoepel.AppFoundation.Sonar;
using AndreGoepel.Core;
using Xunit;

namespace AndreGoepel.AppFoundation.Sonar.Tests;

public sealed class NuGetProvenanceTests
{
    [Fact]
    public async Task Run_ApprovedProducer_BindsArtifactAndRechecksBeforeEveryUpload()
    {
        // Arrange
        using var fixture = new ProvenanceFixture();
        await fixture.InitializeAsync(TestContext.Current.CancellationToken);

        // Act
        var result = await fixture.RunAsync(TestContext.Current.CancellationToken);

        // Assert
        Assert.True(result.IsSuccess, result.Error);
        Assert.Equal(12, fixture.Sink.Accepted.Count);
        Assert.True(result.Value!.DryRun);
        Assert.False(result.Value.ReleaseEligible);
        Assert.Equal(28, fixture.Downloads);
        Assert.All(fixture.StorageAuthorization, Assert.Null);
        Assert.All(fixture.ApiAuthorization, value => Assert.Equal("Bearer fixture-read", value));
    }

    [Theory]
    [InlineData("policy-id")]
    [InlineData("policy-hash")]
    [InlineData("policy-age")]
    [InlineData("policy-jobs")]
    [InlineData("missing-tag")]
    [InlineData("empty-inventory")]
    [InlineData("incomplete-inventory")]
    [InlineData("newer-run")]
    [InlineData("wrong-workflow")]
    [InlineData("new-attempt")]
    [InlineData("wrong-path")]
    [InlineData("wrong-event")]
    [InlineData("wrong-branch")]
    [InlineData("wrong-sha")]
    [InlineData("fork")]
    [InlineData("failed-run")]
    [InlineData("running")]
    [InlineData("expired-run")]
    [InlineData("future-run")]
    [InlineData("unapproved-reusable")]
    [InlineData("workflow-content")]
    [InlineData("workflow-encoding")]
    [InlineData("missing-job")]
    [InlineData("skipped-step")]
    [InlineData("old-job")]
    public async Task Run_UntrustedProducer_BlocksBeforeArtifactOrLogin(string fault)
    {
        // Arrange
        using var fixture = new ProvenanceFixture();
        await fixture.InitializeAsync(TestContext.Current.CancellationToken);
        fixture.Invalidate(fault);

        // Act
        var result = await fixture.RunAsync(TestContext.Current.CancellationToken);

        // Assert
        Assert.True(result.IsFailure, fault);
        Assert.Equal(0, fixture.Downloads);
        Assert.Empty(fixture.Sink.Events);
    }

    [Theory]
    [InlineData("missing-artifact")]
    [InlineData("ambiguous-artifact")]
    [InlineData("incomplete-artifacts")]
    [InlineData("old-attempt-name")]
    [InlineData("expired-artifact")]
    [InlineData("artifact-expiry")]
    [InlineData("oversized-artifact")]
    [InlineData("old-artifact")]
    [InlineData("future-artifact")]
    [InlineData("artifact-run")]
    [InlineData("artifact-sha")]
    [InlineData("artifact-branch")]
    public async Task Run_UntrustedArtifact_BlocksBeforeDownloadOrLogin(string fault)
    {
        // Arrange
        using var fixture = new ProvenanceFixture();
        await fixture.InitializeAsync(TestContext.Current.CancellationToken);
        fixture.Invalidate(fault);

        // Act
        var result = await fixture.RunAsync(TestContext.Current.CancellationToken);

        // Assert
        Assert.True(result.IsFailure, fault);
        Assert.Equal(0, fixture.Downloads);
        Assert.Empty(fixture.Sink.Events);
    }

    [Theory]
    [InlineData("schemaVersion", "2")]
    [InlineData("repository", "\"fork/app-foundation\"")]
    [InlineData("sha", "\"other\"")]
    [InlineData("tag", "\"v9.9.9\"")]
    [InlineData("tagObjectSha", "\"other\"")]
    [InlineData("runId", "999")]
    [InlineData("attempt", "2")]
    [InlineData("readinessRunId", "999")]
    [InlineData("manifestSha256", "\"other\"")]
    [InlineData("preparedAt", "\"2000-01-01T00:00:00Z\"")]
    [InlineData("preparedAt", "\"2099-01-01T00:00:00Z\"")]
    [InlineData("dryRun", "false")]
    [InlineData("releaseEligible", "true")]
    public async Task Run_AuthenticatedButMismatchedOrigin_BlocksBeforeLogin(
        string property,
        string json
    )
    {
        // Arrange
        using var fixture = new ProvenanceFixture();
        await fixture.InitializeAsync(TestContext.Current.CancellationToken);
        fixture.Origin[property] = JsonNode.Parse(json);
        fixture.SealOrigin();

        // Act
        var result = await fixture.RunAsync(TestContext.Current.CancellationToken);

        // Assert
        Assert.True(result.IsFailure, property);
        Assert.Equal(1, fixture.Downloads);
        Assert.Empty(fixture.Sink.Events);
    }

    [Theory]
    [InlineData("digest")]
    [InlineData("zip-entry")]
    [InlineData("malformed-json")]
    [InlineData("api-failure")]
    public async Task Run_UnreadableOrigin_StopsBeforeLogin(string fault)
    {
        // Arrange
        using var fixture = new ProvenanceFixture();
        await fixture.InitializeAsync(TestContext.Current.CancellationToken);
        fixture.Invalidate(fault);

        // Act
        var error = await Record.ExceptionAsync(() =>
            fixture.RunAsync(TestContext.Current.CancellationToken)
        );

        // Assert
        Assert.NotNull(error);
        Assert.Empty(fixture.Sink.Events);
    }

    [Theory]
    [InlineData("newer-run")]
    [InlineData("new-attempt")]
    [InlineData("failed-run")]
    [InlineData("expired-run")]
    [InlineData("missing-artifact")]
    [InlineData("replaced-artifact")]
    public async Task Run_ProvenanceChangesDuringDownload_BlocksBeforeLogin(string fault)
    {
        // Arrange
        using var fixture = new ProvenanceFixture();
        await fixture.InitializeAsync(TestContext.Current.CancellationToken);
        fixture.OnDownload = () => fixture.Invalidate(fault);

        // Act
        var result = await fixture.RunAsync(TestContext.Current.CancellationToken);

        // Assert
        Assert.True(result.IsFailure, fault);
        Assert.Empty(fixture.Sink.Events);
    }

    [Fact]
    public async Task Run_OriginChangesDuringReadiness_BlocksBeforeLogin()
    {
        // Arrange
        using var fixture = new ProvenanceFixture();
        await fixture.InitializeAsync(TestContext.Current.CancellationToken);
        fixture.Handoff.OnVerify = () => fixture.Artifact["id"] = 8002;

        // Act
        var result = await fixture.RunAsync(TestContext.Current.CancellationToken);

        // Assert
        Assert.True(result.IsFailure);
        Assert.Empty(fixture.Sink.Events);
    }

    [Fact]
    public async Task Run_ProducerRerunDuringLogin_BlocksBeforeUpload()
    {
        // Arrange
        using var fixture = new ProvenanceFixture();
        await fixture.InitializeAsync(TestContext.Current.CancellationToken);
        fixture.Sink.OnLogin = () => fixture.Invalidate("new-attempt");

        // Act
        var result = await fixture.RunAsync(TestContext.Current.CancellationToken);

        // Assert
        Assert.True(result.IsFailure);
        Assert.Equal(["login"], fixture.Sink.Events);
        Assert.Empty(fixture.Sink.Accepted);
    }

    [Fact]
    public async Task Run_ProducerChangesAfterUpload_StopsRemainingFiles()
    {
        // Arrange
        using var fixture = new ProvenanceFixture();
        await fixture.InitializeAsync(TestContext.Current.CancellationToken);
        fixture.Sink.OnUpload = _ => fixture.Invalidate("newer-run");

        // Act
        var result = await fixture.RunAsync(TestContext.Current.CancellationToken);

        // Assert
        Assert.True(result.IsFailure);
        Assert.Single(fixture.Sink.Accepted);
    }

    [Theory]
    [InlineData("gate")]
    [InlineData("package")]
    public async Task Run_ValidOriginButInvalidReadinessOrBytes_BlocksBeforeLogin(string fault)
    {
        // Arrange
        using var fixture = new ProvenanceFixture();
        await fixture.InitializeAsync(TestContext.Current.CancellationToken);
        if (fault == "gate")
            fixture.Handoff.Evidence.Main.Responses["qualitygates/project_status"][
                "projectStatus"
            ]!["status"] = "ERROR";
        else
            fixture.Handoff.ChangeFiles("package");

        // Act
        var result = await fixture.RunAsync(TestContext.Current.CancellationToken);

        // Assert
        Assert.True(result.IsFailure, fault);
        Assert.Empty(fixture.Sink.Events);
    }

    internal sealed class ProvenanceFixture : HttpMessageHandler
    {
        private readonly HttpClient client;
        private readonly byte[] workflow;
        private byte[] archive = [];
        internal NuGetHandoffTests.HandoffFixture Handoff { get; } = new();
        internal NuGetPublicationRehearsalTests.RecordingSink Sink { get; }
        internal NuGetPreparationPolicy Policy { get; set; } =
            new(
                900,
                SourceFiles.Hash("reviewed-workflow"u8.ToArray()),
                60,
                new() { ["prepare"] = ["Verify tag push", "Prepare NuGet batch", "Publish origin"] }
            );
        internal JsonNode Origin { get; private set; } = null!;
        internal JsonNode Artifact => Inventory["artifacts"]![0]!;
        internal JsonNode Inventory =>
            Handoff.Evidence.Main.Responses["actions/runs/500/artifacts?per_page=100"];
        internal JsonNode Run => Handoff.Evidence.Main.Responses["actions/runs/500"];
        internal int Downloads { get; private set; }
        internal List<string?> ApiAuthorization { get; } = [];
        internal List<string?> StorageAuthorization { get; } = [];
        internal Action? OnDownload { get; set; }
        internal string Filename { get; set; } = "origin.json";
        internal string? RawOrigin { get; set; }
        internal bool FailApi { get; set; }
        private string RunsPath =>
            $"actions/workflows/{Policy.WorkflowId}/runs?branch=v2.2.0&event=push&head_sha={MainFixture.Sha}&per_page=100";
        private static string WorkflowPath =>
            $"contents/{NuGetPreparationPolicy.WorkflowPath}?ref={MainFixture.Sha}";
        private const string JobsPath = "actions/runs/500/attempts/1/jobs?per_page=100";

        internal ProvenanceFixture(NuGetPreparationPolicy? policy = null, byte[]? workflow = null)
        {
            Policy = policy ?? Policy;
            this.workflow = workflow ?? "reviewed-workflow"u8.ToArray();
            client = new HttpClient(this, disposeHandler: false);
            Sink = new(() => Handoff.Checks);
        }

        internal NuGetConsumer Consumer() =>
            new(
                Handoff,
                Handoff.Evidence.Main,
                new GitHubArtifacts(client, "fixture-read"),
                Policy,
                Handoff.Clock
            );

        internal async Task InitializeAsync(CancellationToken ct)
        {
            await Handoff.InitializeAsync(annotated: true, ct);
            var now = Handoff.Clock.GetUtcNow();
            var responses = Handoff.Evidence.Main.Responses;
            responses[RunsPath] = MainFixture.Node(
                new { total_count = 1, workflow_runs = new[] { new { id = 500 } } }
            );
            responses["actions/runs/500"] = MainFixture.Node(
                new
                {
                    id = 500,
                    workflow_id = Policy.WorkflowId,
                    run_attempt = 1,
                    path = NuGetPreparationPolicy.WorkflowPath,
                    head_sha = MainFixture.Sha,
                    head_branch = "v2.2.0",
                    @event = "push",
                    status = "completed",
                    conclusion = "success",
                    run_started_at = now.AddMinutes(-5),
                    repository = new { full_name = MainPolicy.Repository },
                    head_repository = new { full_name = MainPolicy.Repository },
                    referenced_workflows = Array.Empty<object>(),
                }
            );
            responses[WorkflowPath] = MainFixture.Node(
                new { encoding = "base64", content = Convert.ToBase64String(workflow) }
            );
            responses[JobsPath] = MainFixture.Node(
                new
                {
                    total_count = 1,
                    jobs = new[]
                    {
                        new
                        {
                            id = 501,
                            name = "prepare",
                            status = "completed",
                            conclusion = "success",
                            started_at = now.AddMinutes(-4),
                            steps = Policy
                                .Jobs["prepare"]
                                .Select(name => new
                                {
                                    name,
                                    status = "completed",
                                    conclusion = "success",
                                })
                                .ToArray(),
                        },
                    },
                }
            );
            responses["actions/runs/500/artifacts?per_page=100"] = MainFixture.Node(
                new
                {
                    total_count = 1,
                    artifacts = new[]
                    {
                        new
                        {
                            id = 8001,
                            name = $"nuget-origin-{MainFixture.Sha}-500-1",
                            expired = false,
                            created_at = now.AddMinutes(-1),
                            expires_at = now.AddHours(1),
                            size_in_bytes = 1,
                            digest = "",
                            workflow_run = new
                            {
                                id = 500,
                                head_sha = MainFixture.Sha,
                                head_branch = "v2.2.0",
                            },
                        },
                    },
                }
            );
            Origin = MainFixture.Node(
                new NuGetPreparationOrigin(
                    1,
                    MainPolicy.Repository,
                    MainFixture.Sha,
                    "v2.2.0",
                    Handoff.Input.TagObjectSha,
                    500,
                    1,
                    Handoff.Input.Release.ReadinessRunId,
                    Handoff.Input.ManifestSha256,
                    now.AddMinutes(-2),
                    true,
                    false
                )
            );
            SealOrigin();
        }

        internal void SealOrigin()
        {
            using var bytes = new MemoryStream();
            using (var zip = new ZipArchive(bytes, ZipArchiveMode.Create, leaveOpen: true))
            {
                using var writer = new StreamWriter(
                    zip.CreateEntry(Filename).Open(),
                    new UTF8Encoding(false)
                );
                writer.Write(RawOrigin ?? Origin.ToJsonString());
            }
            archive = bytes.ToArray();
            Artifact["digest"] = "sha256:" + SourceFiles.Hash(archive);
            Artifact["size_in_bytes"] = archive.Length;
        }

        internal Task<Result<NuGetRehearsalReceipt>> RunAsync(CancellationToken ct)
        {
            var provenance = new NuGetProvenance(
                Handoff.Evidence.Main,
                new GitHubArtifacts(client, "fixture-read"),
                Policy,
                Handoff.Clock
            );
            var prepared = new NuGetPreparedHandoff(
                provenance,
                new NuGetHandoff(Handoff, Handoff.Evidence.Main, Handoff.Clock),
                500,
                1
            );
            return new NuGetPublicationRehearsal(prepared, Sink).RunAsync(Handoff.Input, null, ct);
        }

        internal void Invalidate(string fault)
        {
            var responses = Handoff.Evidence.Main.Responses;
            var now = Handoff.Clock.GetUtcNow();
            switch (fault)
            {
                case "policy-id":
                    Policy = Policy with { WorkflowId = 0 };
                    break;
                case "policy-hash":
                    Policy = Policy with { WorkflowSha256 = "invalid" };
                    break;
                case "policy-age":
                    Policy = Policy with { MaximumAgeMinutes = 0 };
                    break;
                case "policy-jobs":
                    Policy = Policy with { Jobs = [] };
                    break;
                case "missing-tag":
                    Handoff.Input = Handoff.Input with
                    {
                        Release = Handoff.Input.Release with { Tag = null },
                    };
                    break;
                case "empty-inventory":
                    responses[RunsPath] = MainFixture.Node(
                        new { total_count = 0, workflow_runs = Array.Empty<object>() }
                    );
                    break;
                case "incomplete-inventory":
                    responses[RunsPath]["total_count"] = 2;
                    break;
                case "newer-run":
                    responses[RunsPath] = MainFixture.Node(
                        new
                        {
                            total_count = 2,
                            workflow_runs = new[] { new { id = 500 }, new { id = 501 } },
                        }
                    );
                    break;
                case "new-attempt":
                    Run["run_attempt"] = 2;
                    break;
                case "wrong-workflow":
                    Run["workflow_id"] = 999;
                    break;
                case "wrong-path":
                    Run["path"] = ".github/workflows/unreviewed.yml";
                    break;
                case "wrong-event":
                    Run["event"] = "workflow_dispatch";
                    break;
                case "wrong-branch":
                    Run["head_branch"] = "main";
                    break;
                case "wrong-sha":
                    Run["head_sha"] = new string('f', 40);
                    break;
                case "fork":
                    Run["head_repository"]!["full_name"] = "fork/app-foundation";
                    break;
                case "failed-run":
                    Run["conclusion"] = "failure";
                    break;
                case "running":
                    Run["status"] = "in_progress";
                    break;
                case "expired-run":
                    Handoff.Clock.Now = now.AddHours(2);
                    break;
                case "future-run":
                    Run["run_started_at"] = now.AddHours(1);
                    break;
                case "unapproved-reusable":
                    Run["referenced_workflows"] = MainFixture.Node(
                        new[] { new { path = "other/workflow@main" } }
                    );
                    break;
                case "workflow-content":
                    responses[WorkflowPath]["content"] = Convert.ToBase64String(
                        "changed"u8.ToArray()
                    );
                    break;
                case "workflow-encoding":
                    responses[WorkflowPath]["encoding"] = "none";
                    break;
                case "missing-job":
                    responses[JobsPath]["jobs"]![0]!["name"] = "other";
                    break;
                case "skipped-step":
                    responses[JobsPath]["jobs"]![0]!["steps"]![1]!["conclusion"] = "skipped";
                    break;
                case "old-job":
                    responses[JobsPath]["jobs"]![0]!["started_at"] = now.AddHours(-1);
                    break;
                case "missing-artifact":
                    Inventory["artifacts"] = new JsonArray();
                    Inventory["total_count"] = 0;
                    break;
                case "ambiguous-artifact":
                    ((JsonArray)Inventory["artifacts"]!).Add(Artifact.DeepClone());
                    Inventory["total_count"] = 2;
                    break;
                case "incomplete-artifacts":
                    Inventory["total_count"] = 2;
                    break;
                case "old-attempt-name":
                    Artifact["name"] = $"nuget-origin-{MainFixture.Sha}-500-0";
                    break;
                case "expired-artifact":
                    Artifact["expired"] = true;
                    break;
                case "artifact-expiry":
                    Artifact["expires_at"] = now.AddMinutes(-1);
                    break;
                case "oversized-artifact":
                    Artifact["size_in_bytes"] = GitHubArtifacts.MaximumBytes + 1;
                    break;
                case "old-artifact":
                    Artifact["created_at"] = now.AddHours(-1);
                    break;
                case "future-artifact":
                    Artifact["created_at"] = now.AddHours(1);
                    break;
                case "artifact-run":
                    Artifact["workflow_run"]!["id"] = 999;
                    break;
                case "artifact-sha":
                    Artifact["workflow_run"]!["head_sha"] = "other";
                    break;
                case "artifact-branch":
                    Artifact["workflow_run"]!["head_branch"] = "other";
                    break;
                case "replaced-artifact":
                    Artifact["id"] = 8002;
                    break;
                case "digest":
                    Artifact["digest"] = "sha256:" + new string('0', 64);
                    break;
                case "zip-entry":
                    Filename = "../origin.json";
                    SealOrigin();
                    break;
                case "malformed-json":
                    RawOrigin = "{";
                    SealOrigin();
                    break;
                case "api-failure":
                    FailApi = true;
                    break;
                default:
                    throw new ArgumentOutOfRangeException(nameof(fault));
            }
        }

        protected override Task<HttpResponseMessage> SendAsync(
            HttpRequestMessage request,
            CancellationToken cancellationToken
        )
        {
            cancellationToken.ThrowIfCancellationRequested();
            if (request.Method != HttpMethod.Get)
                throw new InvalidOperationException("Only artifact reads are allowed.");
            if (request.RequestUri!.Host == "api.github.com")
            {
                ApiAuthorization.Add(request.Headers.Authorization?.ToString());
                return Task.FromResult(
                    new HttpResponseMessage(
                        FailApi ? HttpStatusCode.Forbidden : HttpStatusCode.Redirect
                    )
                    {
                        Headers =
                        {
                            Location = new Uri("https://fixture.blob.core.windows.net/origin"),
                        },
                    }
                );
            }
            if (request.RequestUri.Host != "fixture.blob.core.windows.net")
                throw new InvalidOperationException("Unexpected storage destination.");
            Downloads++;
            StorageAuthorization.Add(request.Headers.Authorization?.ToString());
            var bytes = archive;
            OnDownload?.Invoke();
            return Task.FromResult(
                new HttpResponseMessage(HttpStatusCode.OK) { Content = new ByteArrayContent(bytes) }
            );
        }

        protected override void Dispose(bool disposing)
        {
            if (disposing)
            {
                client.Dispose();
                Handoff.Dispose();
            }
            base.Dispose(disposing);
        }
    }
}
