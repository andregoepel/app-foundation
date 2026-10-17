using System.Text.Json;
using AndreGoepel.Core;

namespace AndreGoepel.AppFoundation.Sonar;

internal interface IReleaseReadiness
{
    Task<Result<ReadinessReceipt>> VerifyAsync(NuGetRelease release, CancellationToken ct);
}

internal sealed class ReleaseReadiness(
    IGitHubClient github,
    IGitHubArtifacts artifacts,
    ISonarClient sonar,
    IProcessRunner processes,
    TimeProvider clock,
    MainPolicy policy,
    AnalysisContract contract,
    string scanToken,
    string verifyToken
) : IReleaseReadiness
{
    public async Task<Result<ReadinessReceipt>> VerifyAsync(
        NuGetRelease release,
        CancellationToken ct
    )
    {
        var source = await CheckSourceAsync(release, ct);
        if (source.IsFailure)
            return Result.Fail<ReadinessReceipt>(source.Error);

        var producer = await CheckProducerAsync(release, ct);
        if (producer.IsFailure)
            return Result.Fail<ReadinessReceipt>(producer.Error);

        var run = producer.Value!;
        var attempt = run.GetProperty("run_attempt").GetInt32();
        var inventory = await github.GetAsync(
            $"actions/runs/{release.ReadinessRunId}/artifacts?per_page=100",
            ct
        );
        var readiness = FindArtifact(inventory, release, attempt, "readiness");
        var analysis = FindArtifact(inventory, release, attempt, "analysis");
        if (readiness.IsFailure || analysis.IsFailure)
            return Result.Fail<ReadinessReceipt>(
                "Missing, expired or ambiguous trusted artifacts."
            );

        var receiptBytes = await artifacts.ReadAsync(readiness.Value, "readiness.json", ct);
        var summaryBytes = await artifacts.ReadAsync(analysis.Value, "summary.json", ct);
        var checkedReceipt = await ReadinessValidation.ValidateAsync(
            release,
            policy,
            contract,
            receiptBytes,
            summaryBytes,
            run,
            clock.GetUtcNow(),
            ct
        );
        if (checkedReceipt.IsFailure)
            return checkedReceipt;

        var receipt = checkedReceipt.Value!;
        var evidence = new GitHubEvidence(github, receipt.Run, policy, clock, Task.Delay);
        var mandatory = await CheckMandatoryAsync(evidence, receipt, ct);
        if (mandatory.IsFailure)
            return Result.Fail<ReadinessReceipt>(mandatory.Error);

        var summary = JsonSerializer.Deserialize<AnalysisSummary>(
            summaryBytes,
            JsonFormat.Options
        )!;
        var verifiedSonar = await CheckSonarAsync(receipt, summary, ct);
        if (verifiedSonar.IsFailure)
            return Result.Fail<ReadinessReceipt>(verifiedSonar.Error);

        // Close races while the other services and artifacts were being read.
        source = await CheckSourceAsync(release, ct);
        producer = await CheckProducerAsync(release, ct);
        mandatory = await CheckMandatoryAsync(evidence, receipt, ct);
        if (
            source.IsFailure
            || producer.IsFailure
            || mandatory.IsFailure
            || producer.Value.GetProperty("run_attempt").GetInt32() != receipt.Run.Attempt
            || clock.GetUtcNow() >= receipt.ExpiresAt
            || await SourceFiles.SnapshotAsync(release.Root, ct) != receipt.SourceSha256
        )
            return Result.Fail<ReadinessReceipt>(
                "Release evidence changed or expired during verification."
            );

        return Result.Ok(receipt);
    }

    private async Task<Result> CheckSourceAsync(NuGetRelease release, CancellationToken ct)
    {
        var checkout = await MainCommand.CheckCheckoutAsync(
            processes,
            release.Root,
            release.Sha,
            ct
        );
        if (checkout.IsFailure)
            return checkout;

        var current = await github.GetAsync("git/ref/heads/main", ct);
        if (current.GetProperty("object").GetProperty("sha").GetString() != release.Sha)
            return Result.Fail("Only the current main tip can be prepared for release.");

        if (release.Tag is null)
            return Result.Ok();

        var reference = await github.GetAsync(
            "git/ref/tags/" + Uri.EscapeDataString(release.Tag),
            ct
        );
        var target = reference.GetProperty("object");
        for (var depth = 0; depth < 8; depth++)
        {
            var type = target.GetProperty("type").GetString();
            var sha = target.GetProperty("sha").GetString();
            if (type == "commit")
                return sha == release.Sha
                    ? Result.Ok()
                    : Result.Fail("The tag does not resolve to current main.");
            if (
                type != "tag"
                || sha is null
                || sha.Length != 40
                || sha.Any(c => !char.IsAsciiHexDigit(c))
            )
                break;

            var tag = await github.GetAsync("git/tags/" + sha, ct);
            target = tag.GetProperty("object");
        }

        return Result.Fail("The release tag could not be resolved to a commit.");
    }

    private async Task<Result<JsonElement>> CheckProducerAsync(
        NuGetRelease release,
        CancellationToken ct
    )
    {
        var runs = await github.GetAsync(
            $"actions/workflows/{NuGetRelease.ReadinessWorkflowId}/runs?branch=main&event=workflow_run&head_sha={release.Sha}&per_page=100",
            ct
        );
        var candidates = runs.GetProperty("workflow_runs").EnumerateArray().ToArray();
        if (
            runs.GetProperty("total_count").GetInt32() != candidates.Length
            || candidates.Length == 0
            || candidates.Max(r => r.GetProperty("id").GetInt64()) != release.ReadinessRunId
        )
            return Result.Fail<JsonElement>(
                "No current, complete readiness producer inventory; revalidate main."
            );

        var run = await github.GetAsync($"actions/runs/{release.ReadinessRunId}", ct);
        if (
            run.GetProperty("id").GetInt64() != release.ReadinessRunId
            || run.GetProperty("workflow_id").GetInt64() != NuGetRelease.ReadinessWorkflowId
            || run.GetProperty("path").GetString() != MainPolicy.WorkflowPath
            || run.GetProperty("head_sha").GetString() != release.Sha
            || run.GetProperty("head_branch").GetString() != "main"
            || run.GetProperty("event").GetString() != "workflow_run"
            || run.GetProperty("repository").GetProperty("full_name").GetString()
                != MainPolicy.Repository
            || run.GetProperty("head_repository").GetProperty("full_name").GetString()
                != MainPolicy.Repository
            || run.GetProperty("status").GetString() != "completed"
            || run.GetProperty("conclusion").GetString() != "success"
        )
            return Result.Fail<JsonElement>(
                "The readiness producer is not the successful trusted main workflow."
            );

        return Result.Ok(run);
    }

    private Result<JsonElement> FindArtifact(
        JsonElement inventory,
        NuGetRelease release,
        int attempt,
        string kind
    )
    {
        var items = inventory.GetProperty("artifacts").EnumerateArray().ToArray();
        var name = $"sonar-{kind}-{release.Sha}-{release.ReadinessRunId}-{attempt}";
        var matches = items.Where(a => a.GetProperty("name").GetString() == name).ToArray();
        if (inventory.GetProperty("total_count").GetInt32() != items.Length || matches.Length != 1)
            return Result.Fail<JsonElement>("Incomplete or ambiguous artifact inventory.");

        var artifact = matches[0];
        var run = artifact.GetProperty("workflow_run");
        if (
            artifact.GetProperty("expired").GetBoolean()
            || artifact.GetProperty("expires_at").GetDateTimeOffset() <= clock.GetUtcNow()
            || artifact.GetProperty("size_in_bytes").GetInt64()
                is <= 0
                    or > GitHubArtifacts.MaximumBytes
            || run.GetProperty("id").GetInt64() != release.ReadinessRunId
            || run.GetProperty("head_sha").GetString() != release.Sha
            || run.GetProperty("head_branch").GetString() != "main"
        )
            return Result.Fail<JsonElement>("Expired artifact or wrong producer source.");

        return Result.Ok(artifact);
    }

    private static async Task<Result> CheckMandatoryAsync(
        GitHubEvidence evidence,
        ReadinessReceipt receipt,
        CancellationToken ct
    )
    {
        var actual = await evidence.ReadAsync(ct, requireCompletedProducer: true);
        return
            actual.IsSuccess
            && JsonSerializer.Serialize(actual.Value, JsonFormat.Options)
                == JsonSerializer.Serialize(receipt.MandatoryWorkflows, JsonFormat.Options)
            ? Result.Ok()
            : Result.Fail(
                "Required CI/E2E evidence is missing, changed or unsuccessful; revalidate main."
            );
    }

    private async Task<Result> CheckSonarAsync(
        ReadinessReceipt receipt,
        AnalysisSummary summary,
        CancellationToken ct
    )
    {
        var status = await sonar.GetAsync("system/status", null, ct);
        if (
            status.GetProperty("id").GetString() != MainPolicy.ServerId
            || status.GetProperty("version").GetString() != contract.ServerVersion
            || status.GetProperty("status").GetString() != "UP"
        )
            return Result.Fail("The expected central Sonar server is unavailable.");

        var task = (
            await sonar.GetAsync("ce/task", scanToken, ct, ("id", receipt.TaskId))
        ).GetProperty("task");
        if (
            task.GetProperty("id").GetString() != receipt.TaskId
            || task.GetProperty("status").GetString() != "SUCCESS"
            || task.GetProperty("componentKey").GetString() != MainPolicy.Project
            || task.GetProperty("analysisId").GetString() != receipt.AnalysisId
        )
            return Result.Fail("The successful Sonar task does not match the receipt.");

        var verifier = new MainSonarPolicy(sonar, policy, contract.Gate, scanToken, verifyToken);
        var assignment = await sonar.GetAsync(
            "qualitygates/get_by_project",
            verifyToken,
            ct,
            ("project", MainPolicy.Project)
        );
        if (assignment.GetProperty("qualityGate").GetProperty("name").GetString() != contract.Gate)
            return Result.Fail("The project no longer uses the reviewed quality gate.");

        var definition = await verifier.CheckDefinitionAsync(ct);
        if (definition.IsFailure)
            return definition;

        summary.Gate = (
            await sonar.GetAsync(
                "qualitygates/project_status",
                verifyToken,
                ct,
                ("analysisId", receipt.AnalysisId)
            )
        ).GetProperty("projectStatus");
        summary.Measures.Clear();
        var analyses = (
            await sonar.GetAsync(
                "project_analyses/search",
                verifyToken,
                ct,
                ("project", MainPolicy.Project),
                ("ps", "1")
            )
        ).GetProperty("analyses");
        if (analyses.GetArrayLength() != 1)
            return Result.Fail("The latest Sonar analysis is missing.");

        var verified = await verifier.VerifyAnalysisAsync(summary, analyses[0], receipt.Run, ct);
        if (verified.IsFailure)
            return verified;

        var latest = (
            await sonar.GetAsync(
                "project_analyses/search",
                verifyToken,
                ct,
                ("project", MainPolicy.Project),
                ("ps", "1")
            )
        ).GetProperty("analyses");
        return
            latest.GetArrayLength() == 1
            && latest[0].GetProperty("key").GetString() == receipt.AnalysisId
            ? Result.Ok()
            : Result.Fail("Sonar analysis changed during verification.");
    }
}
