using System.Text.Json;
using System.Text.Json.Nodes;
using AndreGoepel.AppFoundation.Sonar;
using AndreGoepel.Core;

namespace AndreGoepel.AppFoundation.Sonar.Tests;

internal sealed class ReleaseFixture : IGitHubArtifacts, IProcessRunner, IDisposable
{
    internal MainFixture Main { get; } = new();
    internal string Root { get; } =
        Path.Combine(Path.GetTempPath(), "nuget-tests-" + Guid.NewGuid().ToString("N"));
    internal NuGetRelease Release => new(Root, MainFixture.Sha, "2.2.0", 100, null);
    internal AnalysisContract Contract { get; } =
        new(
            "10.0.402",
            "server-version",
            MainFixture.GateName,
            "Example.slnx",
            new() { ["Example.E2ETests"] = 1 },
            [],
            []
        );
    internal JsonNode Receipt { get; set; } = null!;
    internal JsonNode Summary { get; set; } = null!;
    internal bool Dirty { get; set; }
    internal string Head { get; set; } = MainFixture.Sha;
    internal Action<string>? OnReadArtifact { get; set; }
    internal List<string> Downloads { get; } = [];
    internal string ProducerInventory =>
        $"actions/workflows/{NuGetRelease.ReadinessWorkflowId}/runs?branch=main&event=workflow_run&head_sha={MainFixture.Sha}&per_page=100";
    internal JsonNode Producer => Main.Responses["actions/runs/100"];
    internal JsonNode ArtifactInventory =>
        Main.Responses["actions/runs/100/artifacts?per_page=100"];

    internal async Task InitializeAsync(CancellationToken ct)
    {
        Directory.CreateDirectory(Path.Combine(Root, "scripts/sonar"));
        await File.WriteAllTextAsync(
            Path.Combine(Root, Contract.Solution),
            "<Solution><Project Path=\"tests/Example.E2ETests/Example.E2ETests.csproj\" /></Solution>",
            ct
        );
        await File.WriteAllTextAsync(
            Path.Combine(Root, "scripts/sonar/contract.json"),
            JsonSerializer.Serialize(Contract, JsonFormat.Options),
            ct
        );
        var sourceHash = await SourceFiles.SnapshotAsync(Root, ct);
        var contractHash = SourceFiles.Hash(
            await File.ReadAllBytesAsync(Path.Combine(Root, "scripts/sonar/contract.json"), ct)
        );
        var workflows = await Main.GitHub().ReadAsync(ct);
        Producer["status"] = "completed";
        Producer["workflow_id"] = NuGetRelease.ReadinessWorkflowId;
        Main.Responses[ProducerInventory] = MainFixture.Node(
            new { total_count = 1, workflow_runs = new[] { new { id = 100 } } }
        );

        Summary = MainFixture.Node(
            new
            {
                schemaVersion = 2,
                releaseEligible = false,
                state = "complete",
                project = MainPolicy.Project,
                serverId = MainPolicy.ServerId,
                sdk = Contract.Sdk,
                sourceSha256 = sourceHash,
                contractSha256 = contractHash,
                startedAt = Main.Now.AddMinutes(-3),
                finishedAt = Main.Now.AddMinutes(-2),
                scannerExitCode = 0,
                taskId = "task-id",
                analysisId = "analysis-id",
                gate = Main.Gate(),
                analyzedProjects = new[] { "tests/Example.E2ETests/Example.E2ETests.csproj" },
                measures = new Dictionary<string, string>(),
                tests = new Dictionary<string, object>
                {
                    ["Example.E2ETests"] = new { discovered = 1, passed = 1 },
                },
            }
        );
        Receipt = MainFixture.Node(
            new ReadinessReceipt(
                1,
                MainPolicy.Repository,
                Main.Identity,
                MainPolicy.WorkflowPath,
                MainFixture.Sha,
                Main.Policy.Version,
                Main.Policy.Fingerprint(),
                contractHash,
                SourceFiles.Hash(Bytes(Summary)),
                sourceHash,
                MainFixture.Baseline,
                "task-id",
                "analysis-id",
                workflows.Value!,
                Main.Now.AddMinutes(-1),
                Main.Now.AddMinutes(30)
            )
        );
        Main.Responses["actions/runs/100/artifacts?per_page=100"] = MainFixture.Node(
            new
            {
                total_count = 2,
                artifacts = new[] { Artifact(1, "readiness"), Artifact(2, "analysis") },
            }
        );
        Main.Responses["system/status"] = MainFixture.Node(
            new
            {
                id = MainPolicy.ServerId,
                version = Contract.ServerVersion,
                status = "UP",
            }
        );
        Main.Responses["ce/task"] = MainFixture.Node(
            new
            {
                task = new
                {
                    id = "task-id",
                    status = "SUCCESS",
                    componentKey = MainPolicy.Project,
                    analysisId = "analysis-id",
                },
            }
        );
        Main.Responses["qualitygates/project_status"] = MainFixture.Node(
            new { projectStatus = Main.Gate() }
        );
        Main.Responses["qualitygates/get_by_project"] = MainFixture.Node(
            new { qualityGate = new { name = Contract.Gate } }
        );
        Main.Responses["project_analyses/search"] = MainFixture.Node(
            new { analyses = new[] { Main.Latest() } }
        );
    }

    internal ReleaseReadiness Verifier() =>
        new(
            Main,
            this,
            Main,
            this,
            TimeProvider.System,
            Main.Policy,
            Contract,
            "scanner",
            "verifier"
        );

    private object Artifact(int id, string kind) =>
        new
        {
            id,
            name = $"sonar-{kind}-{MainFixture.Sha}-100-1",
            expired = false,
            expires_at = Main.Now.AddDays(1),
            size_in_bytes = 1000,
            workflow_run = new
            {
                id = 100,
                head_sha = MainFixture.Sha,
                head_branch = "main",
            },
        };

    internal static byte[] Bytes(JsonNode node) =>
        JsonSerializer.SerializeToUtf8Bytes(node, JsonFormat.Options);

    internal void RehashSummary() =>
        Receipt["analysisReceiptSha256"] = SourceFiles.Hash(Bytes(Summary));

    public Task<byte[]> ReadAsync(JsonElement artifact, string filename, CancellationToken ct)
    {
        ct.ThrowIfCancellationRequested();
        Downloads.Add(filename);
        OnReadArtifact?.Invoke(filename);
        return Task.FromResult(Bytes(filename == "readiness.json" ? Receipt : Summary));
    }

    public Task<Result<CommandOutput>> RunAsync(string root, string[] args, CancellationToken ct)
    {
        ct.ThrowIfCancellationRequested();
        return Task.FromResult(
            Result.Ok(
                new CommandOutput(
                    0,
                    args.Contains("rev-parse") ? Head
                        : Dirty ? " M source.cs"
                        : ""
                )
            )
        );
    }

    public void Dispose() => Directory.Delete(Root, recursive: true);
}
