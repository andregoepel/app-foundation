using System.Globalization;
using System.Text.Json;
using AndreGoepel.Core;

namespace AndreGoepel.AppFoundation.Sonar;

internal sealed record NuGetIntentContext(
    long RunId,
    long ArtifactId,
    string ArchiveDigest,
    string ContentHash
)
{
    internal const string WorkflowPath = ".github/workflows/nuget-publish.yml";
    internal const string WorkflowRef =
        MainPolicy.Repository + "/" + WorkflowPath + "@refs/heads/main";

    internal static Result<NuGetIntentContext> Read(
        NuGetHandoffOptions options,
        Func<string, string?> environment
    )
    {
        var release = options.Input.Release;
        var digest = environment("INTENT_ARTIFACT_DIGEST") ?? "";
        var contentHash = environment("INTENT_SHA256") ?? "";
        if (
            environment("GITHUB_REPOSITORY") != MainPolicy.Repository
            || environment("GITHUB_EVENT_NAME") != "workflow_run"
            || environment("GITHUB_REF") != "refs/heads/main"
            || environment("GITHUB_RUN_ATTEMPT") != "1"
            || environment("GITHUB_SHA") != release.Sha
            || environment("GITHUB_WORKFLOW_SHA") != release.Sha
            || environment("GITHUB_WORKFLOW_REF") != WorkflowRef
            || release.Tag != "v" + release.Version
            || options.PreparationAttempt != 1
            || !TryId(environment("GITHUB_RUN_ID"), out var run)
            || !TryId(environment("INTENT_ARTIFACT_ID"), out var artifact)
            || !IsHash(digest)
            || !IsHash(contentHash)
        )
            return Result.Fail<NuGetIntentContext>(
                "Stored intent requires the current first-attempt production publishing context."
            );

        return Result.Ok(new NuGetIntentContext(run, artifact, "sha256:" + digest, contentHash));
    }

    private static bool TryId(string? value, out long id) =>
        long.TryParse(value, NumberStyles.None, CultureInfo.InvariantCulture, out id) && id > 0;

    private static bool IsHash(string value) =>
        value.Length == 64 && value.All(c => c is >= '0' and <= '9' or >= 'a' and <= 'f');
}

internal sealed class NuGetStoredIntent(
    IGitHubClient github,
    IGitHubArtifacts artifacts,
    TimeProvider clock
)
{
    internal async Task<Result> VerifyAsync(
        NuGetHandoffOptions options,
        NuGetIntentContext pins,
        NuGetHandoffReceipt handoff,
        CancellationToken ct,
        bool completed = false
    )
    {
        var release = options.Input.Release;
        var workflow = await github.GetAsync(
            $"contents/{NuGetIntentContext.WorkflowPath}?ref={release.Sha}",
            ct
        );
        var localWorkflow = Path.Combine(release.Root, NuGetIntentContext.WorkflowPath);
        if (
            !File.Exists(localWorkflow)
            || workflow.GetProperty("encoding").GetString() != "base64"
            || SourceFiles.Hash(
                Convert.FromBase64String(workflow.GetProperty("content").GetString()!)
            ) != SourceFiles.Hash(await File.ReadAllBytesAsync(localWorkflow, ct))
        )
            return Result.Fail(
                "The publication workflow does not match the verified main checkout."
            );

        var initial = await ReadSnapshotAsync(options, pins, completed, ct);
        if (initial.IsFailure)
            return initial;

        var bytes = await artifacts.ReadAsync(initial.Value!.Artifact, "intent.json", ct);
        if (SourceFiles.Hash(bytes) != pins.ContentHash)
            return Result.Fail("Stored intent bytes differ from the verified intent.");

        using var document = JsonDocument.Parse(bytes);
        if (!MatchesIntent(document.RootElement, options, pins, handoff, initial.Value))
            return Result.Fail(
                "Stored intent does not describe the complete verified publication."
            );

        var final = await ReadSnapshotAsync(options, pins, completed, ct);
        return
            final.IsSuccess
            && initial.Value.Started == final.Value!.Started
            && initial.Value.Artifact.GetProperty("created_at").GetDateTimeOffset()
                == final.Value.Artifact.GetProperty("created_at").GetDateTimeOffset()
            ? Result.Ok()
            : Result.Fail("The publishing run or stored intent changed during verification.");
    }

    private async Task<Result<IntentSnapshot>> ReadSnapshotAsync(
        NuGetHandoffOptions options,
        NuGetIntentContext pins,
        bool completed,
        CancellationToken ct
    )
    {
        var run = await github.GetAsync($"actions/runs/{pins.RunId}", ct);
        var registered = await github.GetAsync($"actions/workflows/nuget-publish.yml", ct);
        if (!MatchesRun(run, registered, options, pins, completed))
            return Result.Fail<IntentSnapshot>(
                "The intent producer is not the current approved publishing attempt."
            );

        var replay = await github.GetAsync(
            $"actions/workflows/{registered.GetProperty("id").GetInt64()}/runs?branch=main&head_sha={options.Input.Release.Sha}&per_page=100",
            ct
        );
        var candidates = replay.GetProperty("workflow_runs").EnumerateArray().ToArray();
        if (
            replay.GetProperty("total_count").GetInt32() != 1
            || candidates.Length != 1
            || candidates[0].GetProperty("id").GetInt64() != pins.RunId
            || candidates[0].GetProperty("run_attempt").GetInt32() != 1
        )
            return Result.Fail<IntentSnapshot>(
                "Publication requires exactly one first-attempt run for this main SHA; reconcile any prior attempt."
            );

        var jobs = await github.GetAsync(
            $"actions/runs/{pins.RunId}/attempts/1/jobs?per_page=100",
            ct
        );
        if (completed ? NuGetPublicationOutcome.ReadSteps(jobs).IsFailure : !IsBeforeLogin(jobs))
            return Result.Fail<IntentSnapshot>(
                "Stored intent verification must run after successful intent storage and before login."
            );

        var inventory = await github.GetAsync(
            $"actions/runs/{pins.RunId}/artifacts?per_page=100",
            ct
        );
        var items = inventory.GetProperty("artifacts").EnumerateArray().ToArray();
        var matches = items
            .Where(a => a.GetProperty("name").GetString() == $"nuget-publish-intent-{pins.RunId}-1")
            .ToArray();
        if (inventory.GetProperty("total_count").GetInt32() != items.Length || matches.Length != 1)
            return Result.Fail<IntentSnapshot>(
                "The stored intent inventory is missing, ambiguous or incomplete."
            );

        var artifact = matches[0];
        var started = run.GetProperty("run_started_at").GetDateTimeOffset();
        var created = artifact.GetProperty("created_at").GetDateTimeOffset();
        var producer = artifact.GetProperty("workflow_run");
        if (
            artifact.GetProperty("id").GetInt64() != pins.ArtifactId
            || artifact.GetProperty("digest").GetString() != pins.ArchiveDigest
            || artifact.GetProperty("expired").GetBoolean()
            || artifact.GetProperty("expires_at").GetDateTimeOffset() <= clock.GetUtcNow()
            || artifact.GetProperty("size_in_bytes").GetInt64()
                is <= 0
                    or > GitHubArtifacts.MaximumBytes
            || created < started
            || created > clock.GetUtcNow()
            || producer.GetProperty("id").GetInt64() != pins.RunId
            || producer.GetProperty("head_sha").GetString() != options.Input.Release.Sha
            || producer.GetProperty("head_branch").GetString() != "main"
        )
            return Result.Fail<IntentSnapshot>(
                "The intent artifact is expired or differs from the expected run, identity or digest."
            );

        return Result.Ok(new IntentSnapshot(started, artifact));
    }

    private bool MatchesRun(
        JsonElement run,
        JsonElement registered,
        NuGetHandoffOptions options,
        NuGetIntentContext pins,
        bool completed
    ) =>
        run.GetProperty("id").GetInt64() == pins.RunId
        && run.GetProperty("workflow_id").GetInt64() > 0
        && run.GetProperty("workflow_id").GetInt64() == registered.GetProperty("id").GetInt64()
        && registered.GetProperty("path").GetString() == NuGetIntentContext.WorkflowPath
        && registered.GetProperty("state").GetString() == "active"
        && run.GetProperty("path").GetString() == NuGetIntentContext.WorkflowPath
        && run.GetProperty("event").GetString() == "workflow_run"
        && run.GetProperty("head_branch").GetString() == "main"
        && run.GetProperty("head_sha").GetString() == options.Input.Release.Sha
        && run.GetProperty("repository").GetProperty("full_name").GetString()
            == MainPolicy.Repository
        && run.GetProperty("head_repository").GetProperty("full_name").GetString()
            == MainPolicy.Repository
        && run.GetProperty("run_attempt").GetInt32() == 1
        && (
            completed
                ? run.GetProperty("status").GetString() == "completed"
                    && run.GetProperty("conclusion").GetString()
                        is "success"
                            or "failure"
                            or "cancelled"
                            or "timed_out"
                : run.GetProperty("status").GetString() == "in_progress"
                    && run.GetProperty("conclusion").ValueKind == JsonValueKind.Null
        )
        && run.GetProperty("run_started_at").GetDateTimeOffset() <= clock.GetUtcNow()
        && (
            completed
            || clock.GetUtcNow() - run.GetProperty("run_started_at").GetDateTimeOffset()
                < TimeSpan.FromHours(1)
        )
        && (
            !run.TryGetProperty("referenced_workflows", out var references)
            || references.GetArrayLength() == 0
        );

    private static bool IsBeforeLogin(JsonElement document)
    {
        var jobs = document.GetProperty("jobs").EnumerateArray().ToArray();
        if (document.GetProperty("total_count").GetInt32() != 1 || jobs.Length != 1)
            return false;

        var job = jobs[0];
        var steps = job.GetProperty("steps").EnumerateArray().ToArray();
        var names = new[]
        {
            "Preserve publication intent before login",
            "Reverify after preserving intent",
            "Obtain temporary NuGet credentials",
            "Push each reverified package and symbol file",
        };
        var selected = names
            .Select(name => steps.Where(s => s.GetProperty("name").GetString() == name).ToArray())
            .ToArray();
        if (selected.Any(s => s.Length != 1))
            return false;

        var ordered = selected.Select(s => s[0]).ToArray();
        return job.GetProperty("name").GetString() == "publish"
            && job.GetProperty("status").GetString() == "in_progress"
            && ordered
                .Select(s => s.GetProperty("number").GetInt32())
                .SequenceEqual(ordered.Select(s => s.GetProperty("number").GetInt32()).Order())
            && ordered[0].GetProperty("status").GetString() == "completed"
            && ordered[0].GetProperty("conclusion").GetString() == "success"
            && ordered[1].GetProperty("status").GetString() == "in_progress"
            && ordered
                .Skip(2)
                .All(s =>
                    s.GetProperty("status").GetString() == "pending"
                    && s.GetProperty("conclusion").ValueKind == JsonValueKind.Null
                );
    }

    private static bool MatchesIntent(
        JsonElement intent,
        NuGetHandoffOptions options,
        NuGetIntentContext pins,
        NuGetHandoffReceipt handoff,
        IntentSnapshot snapshot
    )
    {
        var input = options.Input;
        var release = input.Release;
        var expected = new Dictionary<string, string>
        {
            ["kind"] = "publication-intent",
            ["repository"] = MainPolicy.Repository,
            ["sha"] = release.Sha,
            ["tag"] = release.Tag!,
            ["tagObjectSha"] = input.TagObjectSha,
            ["version"] = release.Version,
            ["manifestSha256"] = input.ManifestSha256,
            ["readinessRunId"] = release.ReadinessRunId.ToString(CultureInfo.InvariantCulture),
            ["preparationRunId"] = options.PreparationRunId.ToString(CultureInfo.InvariantCulture),
            ["preparationAttempt"] = "1",
            ["publishRunId"] = pins.RunId.ToString(CultureInfo.InvariantCulture),
            ["publishAttempt"] = "1",
            ["workflowRef"] = NuGetIntentContext.WorkflowRef,
            ["workflowRevision"] = release.Sha,
        };
        var packages = intent
            .GetProperty("packages")
            .Deserialize<PackageEvidence[]>(JsonFormat.Options);
        return intent.GetProperty("schemaVersion").GetInt32() == 1
            && !intent.GetProperty("authorizesRetry").GetBoolean()
            && expected.All(pair => intent.GetProperty(pair.Key).GetString() == pair.Value)
            && intent.GetProperty("createdAt").GetDateTimeOffset() >= snapshot.Started
            && intent.GetProperty("createdAt").GetDateTimeOffset()
                <= snapshot.Artifact.GetProperty("created_at").GetDateTimeOffset()
            && packages is not null
            && packages
                .OrderBy(p => p.File, StringComparer.Ordinal)
                .SequenceEqual(handoff.Packages.OrderBy(p => p.File, StringComparer.Ordinal));
    }

    private sealed record IntentSnapshot(DateTimeOffset Started, JsonElement Artifact);
}
