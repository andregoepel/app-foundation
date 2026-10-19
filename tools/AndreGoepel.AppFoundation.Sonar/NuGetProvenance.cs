using System.Text.Json;
using AndreGoepel.Core;

namespace AndreGoepel.AppFoundation.Sonar;

internal sealed record NuGetPreparationOrigin(
    int SchemaVersion,
    string Repository,
    string Sha,
    string Tag,
    string TagObjectSha,
    long RunId,
    int Attempt,
    long ReadinessRunId,
    string ManifestSha256,
    DateTimeOffset PreparedAt,
    bool DryRun,
    bool ReleaseEligible
);

internal sealed record NuGetProvenanceReceipt(long ArtifactId, string ArchiveDigest, int Attempt);

internal sealed class NuGetProvenance(
    IGitHubClient github,
    IGitHubArtifacts artifacts,
    NuGetPreparationPolicy policy,
    TimeProvider clock
)
{
    internal async Task<Result<NuGetProvenanceReceipt>> VerifyAsync(
        NuGetHandoffInput input,
        long runId,
        int expectedAttempt,
        CancellationToken ct
    )
    {
        if (runId <= 0 || expectedAttempt <= 0 || policy.Validate().IsFailure)
            return Result.Fail<NuGetProvenanceReceipt>(
                "A reviewed preparation policy is required."
            );

        var run = await ReadProducerAsync(input, runId, expectedAttempt, ct);
        if (run.IsFailure)
            return Result.Fail<NuGetProvenanceReceipt>(run.Error);

        var producer = run.Value;
        var attempt = producer.GetProperty("run_attempt").GetInt32();
        var started = producer.GetProperty("run_started_at").GetDateTimeOffset();
        var workflow = await github.GetAsync(
            $"contents/{NuGetPreparationPolicy.WorkflowPath}?ref={Uri.EscapeDataString(input.Release.Sha)}",
            ct
        );
        if (
            workflow.GetProperty("encoding").GetString() != "base64"
            || SourceFiles.Hash(
                Convert.FromBase64String(workflow.GetProperty("content").GetString()!)
            ) != policy.WorkflowSha256
        )
            return Result.Fail<NuGetProvenanceReceipt>(
                "The preparation workflow revision is not approved."
            );

        var jobs = await github.GetAsync(
            $"actions/runs/{runId}/attempts/{attempt}/jobs?per_page=100",
            ct
        );
        var checkedJobs = GitHubEvidence.ValidateJobs(
            jobs,
            new WorkflowPolicy(
                policy.WorkflowId,
                NuGetPreparationPolicy.WorkflowPath,
                policy.WorkflowSha256,
                "",
                "",
                policy.Jobs
            ),
            started
        );
        if (checkedJobs.IsFailure)
            return Result.Fail<NuGetProvenanceReceipt>(checkedJobs.Error);

        var inventory = await github.GetAsync($"actions/runs/{runId}/artifacts?per_page=100", ct);
        var artifact = FindOrigin(inventory, input, runId, attempt, started);
        if (artifact.IsFailure)
            return Result.Fail<NuGetProvenanceReceipt>(artifact.Error);

        var bytes = await artifacts.ReadAsync(artifact.Value, "origin.json", ct);
        var origin = JsonSerializer.Deserialize<NuGetPreparationOrigin>(bytes, JsonFormat.Options);
        if (!MatchesOrigin(origin, input, runId, attempt, started, artifact.Value))
            return Result.Fail<NuGetProvenanceReceipt>(
                "The authenticated preparation origin does not match this batch."
            );

        var finalRun = await ReadProducerAsync(input, runId, expectedAttempt, ct);
        var finalInventory = await github.GetAsync(
            $"actions/runs/{runId}/artifacts?per_page=100",
            ct
        );
        var finalArtifact = FindOrigin(finalInventory, input, runId, attempt, started);
        if (
            finalRun.IsFailure
            || finalRun.Value.GetProperty("run_attempt").GetInt32() != attempt
            || finalRun.Value.GetProperty("run_started_at").GetDateTimeOffset() != started
            || finalArtifact.IsFailure
            || finalArtifact.Value.GetProperty("id").GetInt64()
                != artifact.Value.GetProperty("id").GetInt64()
            || finalArtifact.Value.GetProperty("digest").GetString()
                != artifact.Value.GetProperty("digest").GetString()
        )
            return Result.Fail<NuGetProvenanceReceipt>(
                "Preparation changed while its origin was being read."
            );

        return Result.Ok(
            new NuGetProvenanceReceipt(
                artifact.Value.GetProperty("id").GetInt64(),
                artifact.Value.GetProperty("digest").GetString()!,
                attempt
            )
        );
    }

    private async Task<Result<JsonElement>> ReadProducerAsync(
        NuGetHandoffInput input,
        long runId,
        int expectedAttempt,
        CancellationToken ct
    )
    {
        var release = input.Release;
        if (!NuGetRelease.IsPreparationTag(release.Tag, release.Version))
            return Result.Fail<JsonElement>(
                "Preparation provenance requires the exact release tag."
            );

        var inventory = await github.GetAsync(
            $"actions/workflows/{policy.WorkflowId}/runs?branch={Uri.EscapeDataString(release.Tag!)}&event=push&head_sha={release.Sha}&per_page=100",
            ct
        );
        var runs = inventory.GetProperty("workflow_runs").EnumerateArray().ToArray();
        if (
            inventory.GetProperty("total_count").GetInt32() != runs.Length
            || runs.Length == 0
            || runs.Max(run => run.GetProperty("id").GetInt64()) != runId
        )
            return Result.Fail<JsonElement>(
                "Preparation inventory is incomplete or a newer run superseded this batch."
            );

        var run = await github.GetAsync($"actions/runs/{runId}", ct);
        var started = run.GetProperty("run_started_at").GetDateTimeOffset();
        if (
            run.GetProperty("id").GetInt64() != runId
            || run.GetProperty("workflow_id").GetInt64() != policy.WorkflowId
            || run.GetProperty("path").GetString() != NuGetPreparationPolicy.WorkflowPath
            || run.GetProperty("event").GetString() != "push"
            || run.GetProperty("head_branch").GetString() != release.Tag
            || run.GetProperty("head_sha").GetString() != release.Sha
            || run.GetProperty("repository").GetProperty("full_name").GetString()
                != MainPolicy.Repository
            || run.GetProperty("head_repository").GetProperty("full_name").GetString()
                != MainPolicy.Repository
            || run.GetProperty("run_attempt").GetInt32() != expectedAttempt
            || run.GetProperty("status").GetString() != "completed"
            || run.GetProperty("conclusion").GetString() != "success"
            || started > clock.GetUtcNow()
            || clock.GetUtcNow() - started >= TimeSpan.FromMinutes(policy.MaximumAgeMinutes)
            || (
                run.TryGetProperty("referenced_workflows", out var references)
                && references.GetArrayLength() != 0
            )
        )
            return Result.Fail<JsonElement>(
                "The preparation is not a current, successful approved tag run."
            );

        return Result.Ok(run);
    }

    private Result<JsonElement> FindOrigin(
        JsonElement document,
        NuGetHandoffInput input,
        long runId,
        int attempt,
        DateTimeOffset started
    )
    {
        var artifacts = document.GetProperty("artifacts").EnumerateArray().ToArray();
        var name = $"nuget-origin-{input.Release.Sha}-{runId}-{attempt}";
        var matches = artifacts.Where(a => a.GetProperty("name").GetString() == name).ToArray();
        if (
            document.GetProperty("total_count").GetInt32() != artifacts.Length
            || matches.Length != 1
        )
            return Result.Fail<JsonElement>(
                "Preparation artifact inventory is incomplete or ambiguous."
            );

        var artifact = matches[0];
        var producer = artifact.GetProperty("workflow_run");
        var created = artifact.GetProperty("created_at").GetDateTimeOffset();
        if (
            artifact.GetProperty("id").GetInt64() <= 0
            || artifact.GetProperty("expired").GetBoolean()
            || artifact.GetProperty("expires_at").GetDateTimeOffset() <= clock.GetUtcNow()
            || artifact.GetProperty("size_in_bytes").GetInt64()
                is <= 0
                    or > GitHubArtifacts.MaximumBytes
            || created < started
            || created > clock.GetUtcNow()
            || producer.GetProperty("id").GetInt64() != runId
            || producer.GetProperty("head_sha").GetString() != input.Release.Sha
            || producer.GetProperty("head_branch").GetString() != input.Release.Tag
        )
            return Result.Fail<JsonElement>(
                "The preparation artifact is expired or belongs to another run or attempt."
            );

        return Result.Ok(artifact);
    }

    private static bool MatchesOrigin(
        NuGetPreparationOrigin? origin,
        NuGetHandoffInput input,
        long runId,
        int attempt,
        DateTimeOffset started,
        JsonElement artifact
    ) =>
        origin is not null
        && origin.SchemaVersion == 1
        && origin.Repository == MainPolicy.Repository
        && origin.Sha == input.Release.Sha
        && origin.Tag == input.Release.Tag
        && origin.TagObjectSha == input.TagObjectSha
        && origin.RunId == runId
        && origin.Attempt == attempt
        && origin.ReadinessRunId == input.Release.ReadinessRunId
        && origin.ManifestSha256 == input.ManifestSha256
        && origin.PreparedAt >= started
        && origin.PreparedAt <= artifact.GetProperty("created_at").GetDateTimeOffset()
        && origin.DryRun
        && !origin.ReleaseEligible;
}

internal sealed class NuGetPreparedHandoff(
    NuGetProvenance provenance,
    NuGetHandoff handoff,
    long runId,
    int attempt
) : INuGetHandoff
{
    public async Task<Result<NuGetHandoffReceipt>> VerifyAsync(
        NuGetHandoffInput input,
        CancellationToken ct
    )
    {
        var before = await provenance.VerifyAsync(input, runId, attempt, ct);
        if (before.IsFailure)
            return Result.Fail<NuGetHandoffReceipt>(before.Error);

        var result = await handoff.VerifyAsync(input, ct);
        if (result.IsFailure)
            return result;

        var after = await provenance.VerifyAsync(input, runId, attempt, ct);
        return after.IsSuccess && before.Value == after.Value
            ? result
            : Result.Fail<NuGetHandoffReceipt>(
                "Preparation provenance changed during handoff verification."
            );
    }
}
