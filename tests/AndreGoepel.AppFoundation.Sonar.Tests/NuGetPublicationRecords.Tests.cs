using System.IO.Compression;
using System.Net;
using System.Text.Json;
using System.Text.Json.Nodes;
using AndreGoepel.AppFoundation.Sonar;
using AndreGoepel.Core;
using Xunit;

namespace AndreGoepel.AppFoundation.Sonar.Tests;

public sealed class NuGetPublicationRecordsTests
{
    [Theory]
    [InlineData("valid")]
    [InlineData("missing-outcome")]
    [InlineData("changed-package")]
    [InlineData("changed-gate")]
    public async Task Command_Inspection_RequiresAuthenticatedUnchangedHandoff(string scenario)
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
        var options = new NuGetHandoffOptions(preparation.Handoff.Input, 500, 1, null, true);
        var verified = await preparation
            .Consumer()
            .VerifyAsync(options, TestContext.Current.CancellationToken);
        Assert.True(verified.IsSuccess, verified.Error);
        using var records = new RecordFixture(
            options,
            verified.Value!,
            preparation.Handoff.Clock.GetUtcNow()
        );
        if (scenario == "missing-outcome")
            records.SetScenario("missing");
        foreach (var pair in records.Intent.Responses)
            preparation.Handoff.Evidence.Main.Responses[pair.Key] = pair.Value;
        records.OnDownload = () =>
        {
            if (scenario == "changed-package")
                preparation.Handoff.ChangeFiles("package");
            if (scenario == "changed-gate")
                preparation.Handoff.Evidence.Main.Responses["qualitygates/project_status"][
                    "projectStatus"
                ]!["status"] = "ERROR";
        };
        var consumer = new NuGetConsumer(
            preparation.Handoff,
            preparation.Handoff.Evidence.Main,
            new RoutingArtifacts(preparation.ArtifactReader(), records),
            preparation.Policy,
            preparation.Handoff.Clock
        );
        using var output = new StringWriter();

        // Act
        var result = await NuGetCommand.RunHandoffAsync(
            consumer,
            options with
            {
                Inspection = records.Intent.Pins,
            },
            output,
            TestContext.Current.CancellationToken
        );

        // Assert
        Assert.Equal(scenario is "valid" or "missing-outcome", result.IsSuccess);
        if (result.IsSuccess)
        {
            using var document = JsonDocument.Parse(output.ToString());
            Assert.False(document.RootElement.GetProperty("authorizesRetry").GetBoolean());
            Assert.False(document.RootElement.GetProperty("releaseEligible").GetBoolean());
        }
        else
            Assert.Empty(output.ToString());
    }

    [Theory]
    [InlineData("success", true, "cli-success")]
    [InlineData("missing", false, "unrecorded")]
    [InlineData("login-failed", true, "not-dispatched")]
    [InlineData("uncertain", true, "uncertain")]
    [InlineData("cancelled", true, "uncertain")]
    [InlineData("unrecorded", true, "unrecorded")]
    public async Task Inspect_CompletedPublication_ReportsObservationsWithoutRetryAuthority(
        string scenario,
        bool available,
        string firstObservation
    )
    {
        // Arrange
        using var fixture = new RecordFixture();
        fixture.SetScenario(scenario);

        // Act
        var result = await fixture.InspectAsync();

        // Assert
        Assert.True(result.IsSuccess, result.Error);
        var report = result.Value!;
        Assert.Equal(available, report.OutcomesAvailable);
        Assert.Equal(12, report.Packages.Length);
        Assert.Equal(firstObservation, report.Packages[0].CliObservation);
        Assert.False(report.AuthorizesRetry);
        Assert.False(report.ReleaseEligible);
        Assert.False(report.RemoteAcceptanceVerified);
        Assert.All(report.Packages, p => Assert.False(p.RemoteAcceptanceVerified));
        Assert.Equal(available ? 1 : 0, fixture.Downloads);
        Assert.True(fixture.Intent.Downloads >= 2);
    }

    [Theory]
    [InlineData("schemaVersion", "2")]
    [InlineData("kind", "\"other\"")]
    [InlineData("intentSha256", "\"wrong\"")]
    [InlineData("intentArtifactId", "\"701\"")]
    [InlineData("intentArtifactDigest", "\"wrong\"")]
    [InlineData("repository", "\"someone/fork\"")]
    [InlineData("sha", "\"wrong\"")]
    [InlineData("publishRunId", "\"601\"")]
    [InlineData("publishAttempt", "\"2\"")]
    [InlineData("workflowRef", "\"unknown\"")]
    [InlineData("workflowRevision", "\"wrong\"")]
    [InlineData("loginOutcome", "\"failure\"")]
    [InlineData("pushOutcome", "\"failure\"")]
    [InlineData("authorizesRetry", "true")]
    [InlineData("remoteAcceptanceVerified", "true")]
    [InlineData("recordedAt", "\"2020-01-01T00:00:00Z\"")]
    [InlineData("recordedAt", "\"2099-01-01T00:00:00Z\"")]
    public async Task Inspect_OutcomeDoesNotBindToTrustedIntent_Blocks(string property, string json)
    {
        // Arrange
        using var fixture = new RecordFixture();
        fixture.Outcome[property] = JsonNode.Parse(json);
        fixture.Seal();

        // Act
        var result = await fixture.InspectAsync();

        // Assert
        Assert.True(result.IsFailure);
    }

    [Theory]
    [InlineData("missing-file")]
    [InlineData("duplicate-file")]
    [InlineData("reordered-files")]
    [InlineData("hash")]
    [InlineData("feed")]
    [InlineData("version")]
    [InlineData("observation")]
    [InlineData("events")]
    [InlineData("resume-after-error")]
    [InlineData("success-with-missing-log")]
    [InlineData("invalid-exit")]
    [InlineData("events-after-skipped-login")]
    public async Task Inspect_InconsistentPackageOrDispatchRecord_Blocks(string fault)
    {
        // Arrange
        using var fixture = new RecordFixture();
        var packages = fixture.Outcome["packages"]!.AsArray();
        switch (fault)
        {
            case "missing-file":
                packages.RemoveAt(0);
                break;
            case "duplicate-file":
                packages[1] = packages[0]!.DeepClone();
                break;
            case "reordered-files":
                var first = packages[0]!.DeepClone();
                packages.RemoveAt(0);
                packages.Add(first);
                break;
            case "hash":
                packages[0]!["sha256"] = new string('f', 64);
                break;
            case "feed":
                packages[0]!["feed"] = "https://unapproved.example";
                break;
            case "version":
                packages[0]!["version"] = "2.2.1";
                break;
            case "observation":
                packages[0]!["cliObservation"] = "remote-success";
                break;
            case "events":
                packages[0]!["events"] = JsonNode.Parse("[\"cli-success\"]");
                break;
            case "resume-after-error":
                fixture.SetScenario("uncertain");
                packages[1]!["cliObservation"] = "cli-success";
                packages[1]!["events"] = JsonNode.Parse("[\"started\",\"cli-success\"]");
                break;
            case "success-with-missing-log":
                packages[0]!["events"] = new JsonArray();
                break;
            case "invalid-exit":
                fixture.SetScenario("uncertain");
                packages[0]!["events"] = JsonNode.Parse("[\"started\",\"uncertain-exit-0\"]");
                break;
            case "events-after-skipped-login":
                fixture.SetScenario("login-failed");
                packages[0]!["events"] = JsonNode.Parse("[\"started\"]");
                break;
        }
        fixture.Seal();

        // Act
        var result = await fixture.InspectAsync();

        // Assert
        Assert.True(result.IsFailure);
    }

    [Theory]
    [InlineData("expired")]
    [InlineData("elapsed-expiry")]
    [InlineData("future")]
    [InlineData("wrong-run")]
    [InlineData("wrong-sha")]
    [InlineData("wrong-branch")]
    [InlineData("empty")]
    [InlineData("oversized")]
    [InlineData("duplicate")]
    [InlineData("incomplete")]
    [InlineData("wrong-digest")]
    [InlineData("failed-storage")]
    public async Task Inspect_UntrustedOutcomeArtifact_Blocks(string fault)
    {
        // Arrange
        using var fixture = new RecordFixture();
        var artifact = fixture.Artifact;
        switch (fault)
        {
            case "expired":
                artifact["expired"] = true;
                break;
            case "elapsed-expiry":
                artifact["expires_at"] = fixture.Intent.Now;
                break;
            case "future":
                artifact["created_at"] = fixture.Intent.Now.AddDays(1);
                break;
            case "wrong-run":
                artifact["workflow_run"]!["id"] = 601;
                break;
            case "wrong-sha":
                artifact["workflow_run"]!["head_sha"] = "wrong";
                break;
            case "wrong-branch":
                artifact["workflow_run"]!["head_branch"] = "other";
                break;
            case "empty":
                artifact["size_in_bytes"] = 0;
                break;
            case "oversized":
                artifact["size_in_bytes"] = GitHubArtifacts.MaximumBytes + 1;
                break;
            case "duplicate":
                fixture.Intent.Inventory["artifacts"]!.AsArray().Add(artifact.DeepClone());
                fixture.Intent.Inventory["total_count"] = 3;
                break;
            case "incomplete":
                fixture.Intent.Inventory["total_count"] = 100;
                break;
            case "wrong-digest":
                artifact["digest"] = "sha256:" + new string('f', 64);
                break;
            case "failed-storage":
                fixture.Steps[4]!["conclusion"] = "failure";
                break;
        }

        // Act / Assert
        if (fault == "wrong-digest")
            await Assert.ThrowsAsync<InvalidDataException>(() => fixture.InspectAsync());
        else
            Assert.True((await fixture.InspectAsync()).IsFailure);
    }

    [Theory]
    [InlineData("none")]
    [InlineData("prior")]
    [InlineData("incomplete")]
    [InlineData("wrong-run")]
    [InlineData("rerun")]
    public async Task Verify_ReplayedOrIncompletePublishingInventory_BlocksBeforeIntentDownload(
        string fault
    )
    {
        // Arrange
        using var fixture = new NuGetStoredIntentTests.IntentFixture();
        var inventory = fixture.Responses[
            $"actions/workflows/901/runs?branch=main&head_sha={MainFixture.Sha}&per_page=100"
        ];
        var runs = inventory["workflow_runs"]!.AsArray();
        switch (fault)
        {
            case "none":
                runs.Clear();
                inventory["total_count"] = 0;
                break;
            case "prior":
                runs.Add(MainFixture.Node(new { id = 599, run_attempt = 1 }));
                inventory["total_count"] = 2;
                break;
            case "incomplete":
                inventory["total_count"] = 101;
                break;
            case "wrong-run":
                runs[0]!["id"] = 601;
                break;
            case "rerun":
                runs[0]!["run_attempt"] = 2;
                break;
        }

        // Act
        var result = await fixture.VerifyAsync();

        // Assert
        Assert.True(result.IsFailure);
        Assert.Equal(0, fixture.Downloads);
    }

    [Theory]
    [InlineData("missing")]
    [InlineData("duplicate")]
    [InlineData("order")]
    [InlineData("duplicate-number")]
    [InlineData("running")]
    [InlineData("failed-intent-storage")]
    [InlineData("failed-reverification")]
    [InlineData("failed-login")]
    [InlineData("unknown-outcome")]
    public async Task Inspect_CompletedJobContradictsPublication_Blocks(string fault)
    {
        // Arrange
        using var fixture = new RecordFixture();
        switch (fault)
        {
            case "missing":
                fixture.Steps.RemoveAt(2);
                break;
            case "duplicate":
                fixture.Steps.Add(fixture.Steps[2]!.DeepClone());
                break;
            case "order":
                fixture.Steps[2]!["number"] = 20;
                break;
            case "duplicate-number":
                fixture.Steps[2]!["number"] = 8;
                break;
            case "running":
                fixture.Steps[2]!["status"] = "in_progress";
                break;
            case "failed-intent-storage":
                fixture.Steps[0]!["conclusion"] = "failure";
                break;
            case "failed-reverification":
                fixture.Steps[1]!["conclusion"] = "failure";
                break;
            case "failed-login":
                fixture.Steps[2]!["conclusion"] = "failure";
                break;
            case "unknown-outcome":
                fixture.Steps[3]!["conclusion"] = "neutral";
                break;
        }

        // Act
        var result = await fixture.InspectAsync();

        // Assert
        Assert.True(result.IsFailure);
        Assert.Equal(0, fixture.Downloads);
    }

    [Fact]
    public async Task Inspect_OutcomeChangesDuringDownload_RejectsMixedSnapshot()
    {
        // Arrange
        using var fixture = new RecordFixture();
        fixture.OnDownload = () => fixture.Artifact["digest"] = "sha256:" + new string('f', 64);

        // Act
        var result = await fixture.InspectAsync();

        // Assert
        Assert.True(result.IsFailure);
        Assert.Contains("changed", result.Error);
    }

    [Theory]
    [InlineData("valid")]
    [InlineData("missing")]
    [InlineData("extra")]
    [InlineData("run")]
    [InlineData("artifact")]
    [InlineData("archive")]
    [InlineData("content")]
    [InlineData("preparation-rerun")]
    [InlineData("acceptance-tag")]
    public void Parse_InspectionArguments_RequiresCompletePinsAndProductionTag(string scenario)
    {
        // Arrange
        using var fixture = new RunnerFixture();
        string[] args =
        [
            .. NuGetHandoffOptionsTests.Arguments(fixture.Root, "--nuget-inspect-publication"),
            "--publish-run",
            "600",
            "--intent-artifact",
            "700",
            "--intent-archive-sha256",
            new string('a', 64),
            "--intent-sha256",
            new string('b', 64),
        ];
        switch (scenario)
        {
            case "missing":
                args = args[..^1];
                break;
            case "extra":
                args = [.. args, "--retry"];
                break;
            case "run":
                args[22] = "0";
                break;
            case "artifact":
                args[24] = "-1";
                break;
            case "archive":
                args[26] = "bad";
                break;
            case "content":
                args[28] = "bad";
                break;
            case "preparation-rerun":
                args[20] = "2";
                break;
            case "acceptance-tag":
                args[10] = "sonar-poc/nuget/" + new string('a', 32) + "/v2.2.0";
                break;
        }

        // Act
        var parsed = NuGetHandoffOptions.Parse(args);

        // Assert
        Assert.Equal(scenario == "valid", parsed.IsSuccess);
        if (parsed.IsSuccess)
        {
            Assert.True(parsed.Value!.VerifyOnly);
            Assert.NotNull(parsed.Value.Inspection);
            Assert.False(parsed.Value.VerifyStoredIntent);
        }
    }

    internal sealed class RecordFixture : HttpMessageHandler, IGitHubArtifacts
    {
        private byte[] archive = [];
        private readonly HttpClient client;
        internal NuGetStoredIntentTests.IntentFixture Intent { get; }
        internal JsonNode Outcome { get; }
        internal JsonArray Steps =>
            Intent.Responses["actions/runs/600/attempts/1/jobs?per_page=100"]["jobs"]![0]![
                "steps"
            ]!.AsArray();
        internal JsonNode Artifact => Intent.Inventory["artifacts"]![1]!;
        internal int Downloads { get; private set; }
        internal Action? OnDownload { get; set; }

        internal RecordFixture(
            NuGetHandoffOptions? options = null,
            NuGetHandoffReceipt? handoff = null,
            DateTimeOffset? now = null
        )
        {
            Intent = new(options, handoff, now);
            client = new HttpClient(this, disposeHandler: false);
            Intent.Run["status"] = "completed";
            Intent.Run["conclusion"] = "success";
            Intent.Responses["actions/runs/600/attempts/1/jobs?per_page=100"]["jobs"]![0]![
                "status"
            ] = "completed";
            foreach (var step in Steps)
            {
                step!["status"] = "completed";
                step["conclusion"] = "success";
            }
            Steps.Add(
                MainFixture.Node(
                    new
                    {
                        name = "Preserve publication outcomes",
                        number = 12,
                        status = "completed",
                        conclusion = "success",
                    }
                )
            );
            var artifact = Intent.Artifact.DeepClone();
            artifact["id"] = 800;
            artifact["name"] = "nuget-publish-outcomes-600-1";
            artifact["created_at"] = Intent.Now;
            Intent.Inventory["artifacts"]!.AsArray().Add(artifact);
            Intent.Inventory["total_count"] = 2;
            Outcome = MainFixture.Node(
                new
                {
                    schemaVersion = 1,
                    kind = "publication-outcomes",
                    intentSha256 = Intent.Pins.ContentHash,
                    intentArtifactId = "700",
                    intentArtifactDigest = Intent.Pins.ArchiveDigest["sha256:".Length..],
                    repository = MainPolicy.Repository,
                    sha = MainFixture.Sha,
                    publishRunId = "600",
                    publishAttempt = "1",
                    workflowRef = NuGetIntentContext.WorkflowRef,
                    workflowRevision = MainFixture.Sha,
                    recordedAt = Intent.Now,
                    loginOutcome = "success",
                    pushOutcome = "success",
                    authorizesRetry = false,
                    remoteAcceptanceVerified = false,
                    packages = Intent
                        .Handoff.Packages.OrderBy(p => p.File, StringComparer.Ordinal)
                        .Select(p =>
                        {
                            var node = MainFixture.Node(p);
                            node["cliObservation"] = "cli-success";
                            node["events"] = JsonNode.Parse("[\"started\",\"cli-success\"]");
                            return node;
                        })
                        .ToArray(),
                }
            );
            Seal();
        }

        internal void SetScenario(string scenario)
        {
            if (scenario == "missing")
            {
                Intent.Inventory["artifacts"]!.AsArray().RemoveAt(1);
                Intent.Inventory["total_count"] = 1;
                return;
            }
            if (scenario == "success")
                return;
            var login = scenario == "login-failed" ? "failure" : "success";
            var push =
                scenario == "login-failed" ? "skipped"
                : scenario == "cancelled" ? "cancelled"
                : "failure";
            Steps[2]!["conclusion"] = login;
            Steps[3]!["conclusion"] = push;
            Outcome["loginOutcome"] = login;
            Outcome["pushOutcome"] = push;
            foreach (var package in Outcome["packages"]!.AsArray())
            {
                package!["cliObservation"] = push == "skipped" ? "not-dispatched" : "unrecorded";
                package["events"] = new JsonArray();
            }
            if (scenario is "uncertain" or "cancelled")
            {
                Outcome["packages"]![0]!["cliObservation"] = "uncertain";
                Outcome["packages"]![0]!["events"] = JsonNode.Parse(
                    scenario == "cancelled" ? "[\"started\"]" : "[\"started\",\"uncertain-exit-1\"]"
                );
            }
            Seal();
        }

        internal void Seal()
        {
            using var stream = new MemoryStream();
            using (var zip = new ZipArchive(stream, ZipArchiveMode.Create, leaveOpen: true))
            using (var entry = zip.CreateEntry("outcomes.json").Open())
                entry.Write(JsonSerializer.SerializeToUtf8Bytes(Outcome));
            archive = stream.ToArray();
            Artifact["digest"] = "sha256:" + SourceFiles.Hash(archive);
            Artifact["size_in_bytes"] = archive.Length;
        }

        internal Task<Result<NuGetPublicationInspection>> InspectAsync() =>
            new NuGetPublicationRecords(Intent, this, new RecordClock(Intent.Now)).InspectAsync(
                Intent.Options,
                Intent.Pins,
                Intent.Handoff,
                TestContext.Current.CancellationToken
            );

        public Task<byte[]> ReadAsync(
            JsonElement artifact,
            string filename,
            CancellationToken ct
        ) =>
            filename == "intent.json"
                ? Intent.ArtifactReader().ReadAsync(artifact, filename, ct)
                : new GitHubArtifacts(client, "fixture-read").ReadAsync(artifact, filename, ct);

        protected override Task<HttpResponseMessage> SendAsync(
            HttpRequestMessage request,
            CancellationToken cancellationToken
        )
        {
            if (request.RequestUri!.Host == "api.github.com")
                return Task.FromResult(
                    new HttpResponseMessage(HttpStatusCode.Redirect)
                    {
                        Headers =
                        {
                            Location = new Uri("https://fixture.blob.core.windows.net/outcomes"),
                        },
                    }
                );
            Downloads++;
            OnDownload?.Invoke();
            return Task.FromResult(
                new HttpResponseMessage(HttpStatusCode.OK)
                {
                    Content = new ByteArrayContent(archive),
                }
            );
        }

        protected override void Dispose(bool disposing)
        {
            if (disposing)
            {
                client.Dispose();
                Intent.Dispose();
            }
            base.Dispose(disposing);
        }
    }

    private sealed class RecordClock(DateTimeOffset now) : TimeProvider
    {
        public override DateTimeOffset GetUtcNow() => now;
    }

    private sealed class RoutingArtifacts(IGitHubArtifacts preparation, IGitHubArtifacts records)
        : IGitHubArtifacts
    {
        public Task<byte[]> ReadAsync(
            JsonElement artifact,
            string filename,
            CancellationToken ct
        ) =>
            (filename is "intent.json" or "outcomes.json" ? records : preparation).ReadAsync(
                artifact,
                filename,
                ct
            );
    }
}
