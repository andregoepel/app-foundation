using System.IO.Compression;
using System.Net;
using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;
using AndreGoepel.AppFoundation.Sonar;
using Xunit;

namespace AndreGoepel.AppFoundation.Sonar.Tests;

public sealed class NuGetStoredIntentTests
{
    [Theory]
    [InlineData("valid")]
    [InlineData("missing-context")]
    [InlineData("missing-artifact")]
    [InlineData("package-changed")]
    [InlineData("gate-changed")]
    public async Task Command_StoredIntentVerification_ReturnsReceiptOnlyForStillValidHandoff(
        string scenario
    )
    {
        // Arrange
        using var preparation = new NuGetProvenanceTests.ProvenanceFixture();
        var root = preparation.Handoff.Evidence.Root;
        Directory.CreateDirectory(Path.Combine(root, ".github/workflows"));
        File.WriteAllText(
            Path.Combine(root, NuGetIntentContext.WorkflowPath),
            "reviewed-publishing-workflow"
        );
        await preparation.InitializeAsync(TestContext.Current.CancellationToken);
        var options = new NuGetHandoffOptions(preparation.Handoff.Input, 500, 1, null, true, true);
        var verified = await preparation
            .Consumer()
            .VerifyAsync(options, TestContext.Current.CancellationToken);
        Assert.True(verified.IsSuccess, verified.Error);
        using var intent = new IntentFixture(
            options,
            verified.Value!,
            preparation.Handoff.Clock.GetUtcNow()
        );
        foreach (var pair in intent.Responses)
            preparation.Handoff.Evidence.Main.Responses[pair.Key] = pair.Value;
        using var intentClient = new HttpClient(intent, disposeHandler: false);
        var consumer = new NuGetConsumer(
            preparation.Handoff,
            preparation.Handoff.Evidence.Main,
            new IntentRoutingArtifacts(
                preparation.ArtifactReader(),
                new GitHubArtifacts(intentClient, "fixture-read")
            ),
            preparation.Policy,
            preparation.Handoff.Clock
        );
        var environment = NuGetIntentContextTests.EnvironmentValues();
        environment["INTENT_ARTIFACT_DIGEST"] = intent.Pins.ArchiveDigest["sha256:".Length..];
        environment["INTENT_SHA256"] = intent.Pins.ContentHash;
        if (scenario == "missing-context")
            environment.Remove("GITHUB_RUN_ID");
        if (scenario == "missing-artifact")
            intent.Artifact["name"] = "unrelated";
        intent.OnDownload = () =>
        {
            if (scenario == "package-changed")
                preparation.Handoff.ChangeFiles("package");
            if (scenario == "gate-changed")
                preparation.Handoff.Evidence.Main.Responses["qualitygates/project_status"][
                    "projectStatus"
                ]!["status"] = "ERROR";
        };
        using var output = new StringWriter();

        // Act
        var result = await NuGetCommand.RunHandoffAsync(
            consumer,
            options,
            output,
            TestContext.Current.CancellationToken,
            name => environment.GetValueOrDefault(name)
        );

        // Assert
        Assert.Equal(scenario == "valid", result.IsSuccess);
        if (scenario == "valid")
        {
            using var receipt = JsonDocument.Parse(output.ToString());
            Assert.False(receipt.RootElement.GetProperty("releaseEligible").GetBoolean());
            Assert.True(receipt.RootElement.GetProperty("dryRun").GetBoolean());
            Assert.Equal(12, receipt.RootElement.GetProperty("packages").GetArrayLength());
        }
        else
            Assert.Empty(output.ToString());
    }

    [Fact]
    public async Task Verify_AuthenticatedCompleteIntent_ReadsStoredBytesBeforeSuccess()
    {
        // Arrange
        using var fixture = new IntentFixture();

        // Act
        var result = await fixture.VerifyAsync();

        // Assert
        Assert.True(result.IsSuccess, result.Error);
        Assert.Equal(1, fixture.Downloads);
        Assert.Equal(2, fixture.RunReads);
        Assert.Equal(["Bearer fixture-read", null], fixture.Authorization);
    }

    [Theory]
    [InlineData("id", "601")]
    [InlineData("workflow_id", "999")]
    [InlineData("path", "\".github/workflows/other.yml\"")]
    [InlineData("event", "\"workflow_dispatch\"")]
    [InlineData("head_branch", "\"feature/untrusted\"")]
    [InlineData("head_sha", "\"ffffffffffffffffffffffffffffffffffffffff\"")]
    [InlineData("run_attempt", "2")]
    [InlineData("status", "\"completed\"")]
    [InlineData("conclusion", "\"success\"")]
    [InlineData("repository", "{\"full_name\":\"fork/app-foundation\"}")]
    [InlineData("head_repository", "{\"full_name\":\"fork/app-foundation\"}")]
    [InlineData("referenced_workflows", "[{}]")]
    [InlineData("run_started_at", "\"2026-01-01T10:00:00Z\"")]
    [InlineData("run_started_at", "\"2026-01-01T13:00:00Z\"")]
    public async Task Verify_UntrustedRun_BlocksBeforeDownload(string property, string json)
    {
        // Arrange
        using var fixture = new IntentFixture();
        fixture.Run[property] = JsonNode.Parse(json);

        // Act
        var result = await fixture.VerifyAsync();

        // Assert
        Assert.True(result.IsFailure);
        Assert.Equal(0, fixture.Downloads);
    }

    [Theory]
    [InlineData("missing")]
    [InlineData("duplicate")]
    [InlineData("incomplete")]
    [InlineData("wrong-id")]
    [InlineData("digest")]
    [InlineData("expired")]
    [InlineData("elapsed-expiry")]
    [InlineData("old-attempt")]
    [InlineData("future")]
    [InlineData("wrong-run")]
    [InlineData("wrong-sha")]
    [InlineData("wrong-branch")]
    [InlineData("empty")]
    [InlineData("oversized")]
    public async Task Verify_UntrustedArtifact_BlocksBeforeDownload(string fault)
    {
        // Arrange
        using var fixture = new IntentFixture();
        switch (fault)
        {
            case "missing":
                fixture.Artifact["name"] = "unrelated";
                break;
            case "duplicate":
                fixture.Inventory["artifacts"]!.AsArray().Add(fixture.Artifact.DeepClone());
                fixture.Inventory["total_count"] = 2;
                break;
            case "incomplete":
                fixture.Inventory["total_count"] = 101;
                break;
            case "wrong-id":
                fixture.Artifact["id"] = 701;
                break;
            case "digest":
                fixture.Artifact["digest"] = "sha256:" + new string('f', 64);
                break;
            case "expired":
                fixture.Artifact["expired"] = true;
                break;
            case "elapsed-expiry":
                fixture.Artifact["expires_at"] = fixture.Now;
                break;
            case "old-attempt":
                fixture.Artifact["created_at"] = fixture.Now.AddMinutes(-10);
                break;
            case "future":
                fixture.Artifact["created_at"] = fixture.Now.AddMinutes(1);
                break;
            case "wrong-run":
                fixture.Artifact["workflow_run"]!["id"] = 601;
                break;
            case "wrong-sha":
                fixture.Artifact["workflow_run"]!["head_sha"] = new string('f', 40);
                break;
            case "wrong-branch":
                fixture.Artifact["workflow_run"]!["head_branch"] = "other";
                break;
            case "empty":
                fixture.Artifact["size_in_bytes"] = 0;
                break;
            case "oversized":
                fixture.Artifact["size_in_bytes"] = GitHubArtifacts.MaximumBytes + 1;
                break;
        }

        // Act
        var result = await fixture.VerifyAsync();

        // Assert
        Assert.True(result.IsFailure);
        Assert.Equal(0, fixture.Downloads);
    }

    [Theory]
    [InlineData("schemaVersion", "2")]
    [InlineData("kind", "\"publication-outcomes\"")]
    [InlineData("repository", "\"other/repo\"")]
    [InlineData("sha", "\"wrong\"")]
    [InlineData("tag", "\"v9.9.9\"")]
    [InlineData("tagObjectSha", "\"wrong\"")]
    [InlineData("version", "\"9.9.9\"")]
    [InlineData("manifestSha256", "\"wrong\"")]
    [InlineData("readinessRunId", "\"999\"")]
    [InlineData("preparationRunId", "\"999\"")]
    [InlineData("preparationAttempt", "\"2\"")]
    [InlineData("publishRunId", "\"999\"")]
    [InlineData("publishAttempt", "\"2\"")]
    [InlineData("workflowRef", "\"other\"")]
    [InlineData("workflowRevision", "\"wrong\"")]
    [InlineData("authorizesRetry", "true")]
    [InlineData("createdAt", "\"2026-01-01T10:00:00Z\"")]
    [InlineData("createdAt", "\"2026-01-01T12:01:00Z\"")]
    [InlineData("packages", "[]")]
    [InlineData("packages", "null")]
    public async Task Verify_StoredIntentDescribesDifferentPublication_RejectsEvenWithMatchingHashes(
        string property,
        string json
    )
    {
        // Arrange
        using var fixture = new IntentFixture();
        fixture.Intent[property] = JsonNode.Parse(json);
        fixture.Seal();

        // Act
        var result = await fixture.VerifyAsync();

        // Assert
        Assert.True(result.IsFailure);
        Assert.Equal(1, fixture.Downloads);
    }

    [Theory]
    [InlineData("file")]
    [InlineData("packageId")]
    [InlineData("version")]
    [InlineData("sha256")]
    [InlineData("feed")]
    public async Task Verify_StoredPackageMismatch_RejectsEntireBatch(string property)
    {
        // Arrange
        using var fixture = new IntentFixture();
        fixture.Intent["packages"]![0]![property] = "changed";
        fixture.Seal();

        // Act
        var result = await fixture.VerifyAsync();

        // Assert
        Assert.True(result.IsFailure);
    }

    [Theory]
    [InlineData("registered-path")]
    [InlineData("disabled")]
    [InlineData("workflow-bytes")]
    [InlineData("missing-local-workflow")]
    [InlineData("extra-job")]
    [InlineData("job-name")]
    [InlineData("job-status")]
    [InlineData("missing-step")]
    [InlineData("duplicate-step")]
    [InlineData("step-order")]
    [InlineData("failed-storage")]
    [InlineData("verification-completed")]
    [InlineData("login-started")]
    [InlineData("push-started")]
    public async Task Verify_WrongWorkflowOrJobBoundary_RejectsBeforeDownload(string fault)
    {
        // Arrange
        using var fixture = new IntentFixture();
        var job = fixture.Jobs["jobs"]![0]!;
        var steps = job["steps"]!.AsArray();
        switch (fault)
        {
            case "registered-path":
                fixture.Registered["path"] = "other";
                break;
            case "disabled":
                fixture.Registered["state"] = "disabled_manually";
                break;
            case "workflow-bytes":
                fixture.Workflow["content"] = Convert.ToBase64String("other"u8);
                break;
            case "missing-local-workflow":
                File.Delete(Path.Combine(fixture.Root, NuGetIntentContext.WorkflowPath));
                break;
            case "extra-job":
                fixture.Jobs["total_count"] = 2;
                break;
            case "job-name":
                job["name"] = "other";
                break;
            case "job-status":
                job["status"] = "completed";
                break;
            case "missing-step":
                steps.RemoveAt(0);
                break;
            case "duplicate-step":
                steps.Add(steps[0]!.DeepClone());
                break;
            case "step-order":
                steps[0]!["number"] = 99;
                break;
            case "failed-storage":
                steps[0]!["conclusion"] = "failure";
                break;
            case "verification-completed":
                steps[1]!["status"] = "completed";
                break;
            case "login-started":
                steps[2]!["status"] = "in_progress";
                break;
            case "push-started":
                steps[3]!["status"] = "in_progress";
                break;
        }

        // Act
        var result = await fixture.VerifyAsync();

        // Assert
        Assert.True(result.IsFailure);
        Assert.Equal(0, fixture.Downloads);
    }

    [Theory]
    [InlineData("new-attempt")]
    [InlineData("removed")]
    [InlineData("changed-digest")]
    [InlineData("created-at")]
    [InlineData("expired")]
    public async Task Verify_MetadataChangesDuringDownload_RejectsReceipt(string fault)
    {
        // Arrange
        using var fixture = new IntentFixture();
        fixture.OnDownload = () =>
        {
            switch (fault)
            {
                case "new-attempt":
                    fixture.Run["run_attempt"] = 2;
                    break;
                case "removed":
                    fixture.Artifact["name"] = "removed";
                    break;
                case "changed-digest":
                    fixture.Artifact["digest"] = "sha256:" + new string('f', 64);
                    break;
                case "created-at":
                    fixture.Artifact["created_at"] = fixture.Now.AddSeconds(-20);
                    break;
                case "expired":
                    fixture.Artifact["expired"] = true;
                    break;
            }
        };

        // Act
        var result = await fixture.VerifyAsync();

        // Assert
        Assert.True(result.IsFailure);
        Assert.Equal(1, fixture.Downloads);
    }

    [Fact]
    public async Task Verify_ContentHashMismatch_RejectsAuthenticatedArchive()
    {
        // Arrange
        using var fixture = new IntentFixture();
        fixture.Pins = fixture.Pins with { ContentHash = new string('f', 64) };

        // Act
        var result = await fixture.VerifyAsync();

        // Assert
        Assert.True(result.IsFailure);
        Assert.Equal(1, fixture.Downloads);
    }

    [Theory]
    [InlineData("wrong-entry")]
    [InlineData("bad-archive-digest")]
    [InlineData("api-error")]
    public async Task Verify_UnreadableStoredArchive_FailsClosed(string fault)
    {
        // Arrange
        using var fixture = new IntentFixture();
        fixture.Seal(fault == "wrong-entry" ? "other.json" : "intent.json");
        fixture.DamageArchive = fault == "bad-archive-digest";
        fixture.FailApi = fault == "api-error";

        // Act / Assert
        await Assert.ThrowsAsync<InvalidDataException>(() => fixture.VerifyAsync());
    }

    [Fact]
    public async Task Verify_Cancelled_StopsWithoutSuccess()
    {
        // Arrange
        using var fixture = new IntentFixture();
        using var cancellation = new CancellationTokenSource();
        await cancellation.CancelAsync();

        // Act / Assert
        await Assert.ThrowsAsync<OperationCanceledException>(() =>
            fixture.VerifyAsync(cancellation.Token)
        );
    }

    private sealed class IntentFixture : HttpMessageHandler, IGitHubClient
    {
        private readonly RunnerFixture files = new();
        private readonly HttpClient client;
        private byte[] archive = [];
        internal DateTimeOffset Now { get; }
        internal string Root => files.Root;
        internal NuGetHandoffOptions Options { get; }
        internal NuGetHandoffReceipt Handoff { get; }
        internal NuGetIntentContext Pins { get; set; } = null!;
        internal Dictionary<string, JsonNode> Responses { get; } = [];
        internal JsonNode Intent { get; }
        internal JsonNode Run => Responses["actions/runs/600"];
        internal JsonNode Registered => Responses["actions/workflows/nuget-publish.yml"];
        internal JsonNode Workflow =>
            Responses[$"contents/{NuGetIntentContext.WorkflowPath}?ref={MainFixture.Sha}"];
        internal JsonNode Jobs => Responses["actions/runs/600/attempts/1/jobs?per_page=100"];
        internal JsonNode Inventory => Responses["actions/runs/600/artifacts?per_page=100"];
        internal JsonNode Artifact => Inventory["artifacts"]![0]!;
        internal int Downloads { get; private set; }
        internal int RunReads { get; private set; }
        internal List<string?> Authorization { get; } = [];
        internal Action? OnDownload { get; set; }
        internal bool DamageArchive { get; set; }
        internal bool FailApi { get; set; }

        internal IntentFixture(
            NuGetHandoffOptions? options = null,
            NuGetHandoffReceipt? handoff = null,
            DateTimeOffset? now = null
        )
        {
            Now =
                now
                ?? DateTimeOffset.Parse(
                    "2026-01-01T12:00:00Z",
                    System.Globalization.CultureInfo.InvariantCulture
                );
            client = new HttpClient(this, disposeHandler: false);
            Options =
                options
                ?? NuGetHandoffOptions
                    .Parse(NuGetHandoffOptionsTests.Arguments(Root, "--nuget-verify-intent"))
                    .Value!;
            Handoff =
                handoff
                ?? new(
                    NuGetRelease
                        .Packages.SelectMany(id =>
                            new[] { "nupkg", "snupkg" }.Select(extension => new PackageEvidence(
                                $"{id}.2.2.0.{extension}",
                                id,
                                "2.2.0",
                                new string('b', 64),
                                NuGetRelease.Feed
                            ))
                        )
                        .ToArray()
                );
            Directory.CreateDirectory(
                Path.Combine(Options.Input.Release.Root, ".github/workflows")
            );
            File.WriteAllText(
                Path.Combine(Options.Input.Release.Root, NuGetIntentContext.WorkflowPath),
                "reviewed-publishing-workflow"
            );
            Responses[$"contents/{NuGetIntentContext.WorkflowPath}?ref={MainFixture.Sha}"] =
                MainFixture.Node(
                    new
                    {
                        encoding = "base64",
                        content = Convert.ToBase64String("reviewed-publishing-workflow"u8),
                    }
                );
            Responses["actions/workflows/nuget-publish.yml"] = MainFixture.Node(
                new
                {
                    id = 901,
                    path = NuGetIntentContext.WorkflowPath,
                    state = "active",
                }
            );
            Responses["actions/runs/600"] = MainFixture.Node(
                new
                {
                    id = 600,
                    workflow_id = 901,
                    path = NuGetIntentContext.WorkflowPath,
                    @event = "workflow_run",
                    head_branch = "main",
                    head_sha = MainFixture.Sha,
                    run_attempt = 1,
                    status = "in_progress",
                    conclusion = (string?)null,
                    run_started_at = Now.AddMinutes(-5),
                    repository = new { full_name = MainPolicy.Repository },
                    head_repository = new { full_name = MainPolicy.Repository },
                }
            );
            Responses["actions/runs/600/attempts/1/jobs?per_page=100"] = MainFixture.Node(
                new
                {
                    total_count = 1,
                    jobs = new[]
                    {
                        new
                        {
                            name = "publish",
                            status = "in_progress",
                            steps = new[]
                            {
                                new
                                {
                                    name = "Preserve publication intent before login",
                                    number = 7,
                                    status = "completed",
                                    conclusion = (string?)"success",
                                },
                                new
                                {
                                    name = "Reverify after preserving intent",
                                    number = 8,
                                    status = "in_progress",
                                    conclusion = (string?)null,
                                },
                                new
                                {
                                    name = "Obtain temporary NuGet credentials",
                                    number = 9,
                                    status = "pending",
                                    conclusion = (string?)null,
                                },
                                new
                                {
                                    name = "Push each reverified package and symbol file",
                                    number = 10,
                                    status = "pending",
                                    conclusion = (string?)null,
                                },
                            },
                        },
                    },
                }
            );
            Responses["actions/runs/600/artifacts?per_page=100"] = MainFixture.Node(
                new
                {
                    total_count = 1,
                    artifacts = new[]
                    {
                        new
                        {
                            id = 700,
                            name = "nuget-publish-intent-600-1",
                            expired = false,
                            created_at = Now.AddMinutes(-1),
                            expires_at = Now.AddDays(90),
                            size_in_bytes = 1,
                            digest = "",
                            workflow_run = new
                            {
                                id = 600,
                                head_branch = "main",
                                head_sha = MainFixture.Sha,
                            },
                        },
                    },
                }
            );
            Intent = MainFixture.Node(
                new
                {
                    schemaVersion = 1,
                    kind = "publication-intent",
                    repository = MainPolicy.Repository,
                    sha = MainFixture.Sha,
                    tag = "v2.2.0",
                    tagObjectSha = Options.Input.TagObjectSha,
                    version = "2.2.0",
                    manifestSha256 = Options.Input.ManifestSha256,
                    readinessRunId = "100",
                    preparationRunId = "500",
                    preparationAttempt = "1",
                    publishRunId = "600",
                    publishAttempt = "1",
                    workflowRef = NuGetIntentContext.WorkflowRef,
                    workflowRevision = MainFixture.Sha,
                    createdAt = Now.AddMinutes(-2),
                    packages = Handoff.Packages,
                    authorizesRetry = false,
                }
            );
            Seal();
        }

        internal void Seal(string filename = "intent.json")
        {
            var bytes = Encoding.UTF8.GetBytes(Intent.ToJsonString());
            using var output = new MemoryStream();
            using (var zip = new ZipArchive(output, ZipArchiveMode.Create, leaveOpen: true))
            using (var entry = zip.CreateEntry(filename).Open())
                entry.Write(bytes);
            archive = output.ToArray();
            Artifact["digest"] = "sha256:" + SourceFiles.Hash(archive);
            Artifact["size_in_bytes"] = archive.Length;
            Pins = new(600, 700, "sha256:" + SourceFiles.Hash(archive), SourceFiles.Hash(bytes));
        }

        internal Task<AndreGoepel.Core.Result> VerifyAsync(CancellationToken? ct = null) =>
            new NuGetStoredIntent(
                this,
                new GitHubArtifacts(client, "fixture-read"),
                new IntentClock(Now)
            ).VerifyAsync(Options, Pins, Handoff, ct ?? TestContext.Current.CancellationToken);

        public Task<JsonElement> GetAsync(string path, CancellationToken cancellationToken)
        {
            cancellationToken.ThrowIfCancellationRequested();
            if (path == "actions/runs/600")
                RunReads++;
            return Task.FromResult(JsonSerializer.SerializeToElement(Responses[path]));
        }

        protected override Task<HttpResponseMessage> SendAsync(
            HttpRequestMessage request,
            CancellationToken cancellationToken
        )
        {
            Authorization.Add(request.Headers.Authorization?.ToString());
            if (request.RequestUri!.Host == "api.github.com")
                return Task.FromResult(
                    new HttpResponseMessage(
                        FailApi ? HttpStatusCode.Forbidden : HttpStatusCode.Redirect
                    )
                    {
                        Headers =
                        {
                            Location = new Uri("https://fixture.blob.core.windows.net/intent"),
                        },
                    }
                );

            Downloads++;
            OnDownload?.Invoke();
            return Task.FromResult(
                new HttpResponseMessage(HttpStatusCode.OK)
                {
                    Content = new ByteArrayContent(DamageArchive ? [1, 2, 3] : archive),
                }
            );
        }

        protected override void Dispose(bool disposing)
        {
            if (disposing)
            {
                client.Dispose();
                files.Dispose();
            }
            base.Dispose(disposing);
        }
    }

    private sealed class IntentClock(DateTimeOffset now) : TimeProvider
    {
        public override DateTimeOffset GetUtcNow() => now;
    }

    private sealed class IntentRoutingArtifacts(IGitHubArtifacts origin, IGitHubArtifacts intent)
        : IGitHubArtifacts
    {
        public Task<byte[]> ReadAsync(
            JsonElement artifact,
            string filename,
            CancellationToken ct
        ) => (filename == "intent.json" ? intent : origin).ReadAsync(artifact, filename, ct);
    }
}
