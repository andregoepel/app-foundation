using System.Text.Json;
using AndreGoepel.Core;

namespace AndreGoepel.AppFoundation.Sonar;

internal sealed class NuGetPublicationRecords(
    IGitHubClient github,
    IGitHubArtifacts artifacts,
    TimeProvider clock
)
{
    internal async Task<Result<NuGetPublicationInspection>> InspectAsync(
        NuGetHandoffOptions options,
        NuGetIntentContext pins,
        NuGetHandoffReceipt handoff,
        CancellationToken ct
    )
    {
        var intent = new NuGetStoredIntent(github, artifacts, clock);
        var verified = await intent.VerifyAsync(options, pins, handoff, ct, completed: true);
        if (verified.IsFailure)
            return Result.Fail<NuGetPublicationInspection>(verified.Error);

        var initial = await ReadOutcomeAsync(options.Input.Release, pins.RunId, ct);
        if (initial.IsFailure)
            return Result.Fail<NuGetPublicationInspection>(initial.Error);

        var observations = handoff
            .Packages.Select(p => new NuGetFileObservation(p, "unrecorded"))
            .ToArray();
        string? observedJobs = null;
        if (initial.Value is { } artifact)
        {
            var bytes = await artifacts.ReadAsync(artifact, "outcomes.json", ct);
            var run = await github.GetAsync($"actions/runs/{pins.RunId}", ct);
            var jobs = await github.GetAsync(
                $"actions/runs/{pins.RunId}/attempts/1/jobs?per_page=100",
                ct
            );
            var steps = NuGetPublicationOutcome.ReadSteps(jobs);
            observedJobs = jobs.GetRawText();
            if (steps.IsFailure)
                return Result.Fail<NuGetPublicationInspection>(steps.Error);
            var storage = jobs.GetProperty("jobs")[0]
                .GetProperty("steps")
                .EnumerateArray()
                .Where(s => s.GetProperty("name").GetString() == "Preserve publication outcomes")
                .ToArray();
            if (
                storage.Length != 1
                || storage[0].GetProperty("status").GetString() != "completed"
                || storage[0].GetProperty("conclusion").GetString() != "success"
            )
                return Result.Fail<NuGetPublicationInspection>(
                    "The outcome artifact has no successful storage step."
                );

            using var document = JsonDocument.Parse(bytes);
            var outcome = NuGetPublicationOutcome.Validate(
                document.RootElement,
                pins,
                options.Input.Release,
                handoff,
                steps.Value!,
                run.GetProperty("run_started_at").GetDateTimeOffset(),
                artifact.GetProperty("created_at").GetDateTimeOffset()
            );
            if (outcome.IsFailure)
                return Result.Fail<NuGetPublicationInspection>(outcome.Error);
            observations = outcome.Value!;
        }

        verified = await intent.VerifyAsync(options, pins, handoff, ct, completed: true);
        var final = await ReadOutcomeAsync(options.Input.Release, pins.RunId, ct);
        var finalJobs = await github.GetAsync(
            $"actions/runs/{pins.RunId}/attempts/1/jobs?per_page=100",
            ct
        );
        if (
            verified.IsFailure
            || final.IsFailure
            || initial.Value?.GetRawText() != final.Value?.GetRawText()
            || (observedJobs is not null && observedJobs != finalJobs.GetRawText())
        )
            return Result.Fail<NuGetPublicationInspection>(
                "Publication records changed during inspection."
            );

        return Result.Ok(
            new NuGetPublicationInspection(pins.RunId, initial.Value.HasValue, observations)
            {
                AcceptanceOnly = pins.AcceptanceOnly,
            }
        );
    }

    private async Task<Result<JsonElement?>> ReadOutcomeAsync(
        NuGetRelease release,
        long runId,
        CancellationToken ct
    )
    {
        var inventory = await github.GetAsync($"actions/runs/{runId}/artifacts?per_page=100", ct);
        var items = inventory.GetProperty("artifacts").EnumerateArray().ToArray();
        var matches = items
            .Where(a => a.GetProperty("name").GetString() == $"nuget-publish-outcomes-{runId}-1")
            .ToArray();
        if (inventory.GetProperty("total_count").GetInt32() != items.Length || matches.Length > 1)
            return Result.Fail<JsonElement?>(
                "Outcome artifact inventory is incomplete or ambiguous."
            );
        if (matches.Length == 0)
            return Result.Ok<JsonElement?>(null);

        var artifact = matches[0];
        var producer = artifact.GetProperty("workflow_run");
        if (
            artifact.GetProperty("id").GetInt64() <= 0
            || artifact.GetProperty("expired").GetBoolean()
            || artifact.GetProperty("expires_at").GetDateTimeOffset() <= clock.GetUtcNow()
            || artifact.GetProperty("created_at").GetDateTimeOffset() > clock.GetUtcNow()
            || artifact.GetProperty("size_in_bytes").GetInt64()
                is <= 0
                    or > GitHubArtifacts.MaximumBytes
            || producer.GetProperty("id").GetInt64() != runId
            || producer.GetProperty("head_sha").GetString() != release.Sha
            || producer.GetProperty("head_branch").GetString() != "main"
        )
            return Result.Fail<JsonElement?>(
                "The outcome artifact is expired or belongs to another publication."
            );

        return Result.Ok<JsonElement?>(artifact);
    }
}
