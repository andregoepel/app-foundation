using System.Text.Json;
using AndreGoepel.Core;

namespace AndreGoepel.AppFoundation.Sonar;

internal sealed class GitHubEvidence(
    IGitHubClient client,
    MainRunIdentity identity,
    MainPolicy policy,
    TimeProvider clock,
    Func<TimeSpan, CancellationToken, Task> delay
)
{
    internal async Task<Result<WorkflowEvidence[]>> WaitAsync(
        CancellationToken ct,
        int maximumPolls = 60
    )
    {
        for (var poll = 0; poll < maximumPolls; poll++)
        {
            var result = await ReadAsync(ct);
            if (result.IsFailure || result.Value!.Length == policy.Workflows.Length)
                return result;

            Console.WriteLine("Waiting for mandatory main CI/E2E evidence.");
            await delay(TimeSpan.FromSeconds(10), ct);
        }

        return Result.Fail<WorkflowEvidence[]>(
            "Timed out waiting for complete main CI/E2E evidence."
        );
    }

    internal async Task<Result<WorkflowEvidence[]>> ReadAsync(
        CancellationToken ct,
        bool requireCompletedProducer = false
    )
    {
        var current = await client.GetAsync("git/ref/heads/main", ct);
        if (current.GetProperty("object").GetProperty("sha").GetString() != identity.Sha)
            return Result.Fail<WorkflowEvidence[]>(
                "The candidate was superseded by a newer main commit."
            );

        var ownRun = await client.GetAsync($"actions/runs/{identity.RunId}", ct);
        if (
            !MatchesSource(ownRun, "workflow_run", MainPolicy.WorkflowPath)
            || ownRun.GetProperty("run_attempt").GetInt32() != identity.Attempt
            || ownRun.GetProperty("status").GetString()
                != (requireCompletedProducer ? "completed" : "in_progress")
            || (
                requireCompletedProducer
                && ownRun.GetProperty("conclusion").GetString() != "success"
            )
        )
            return Result.Fail<WorkflowEvidence[]>(
                "Unexpected readiness workflow identity or attempt."
            );

        var evidence = new List<WorkflowEvidence>();
        foreach (var expected in policy.Workflows)
        {
            var runs = await client.GetAsync(
                $"actions/workflows/{expected.Id}/runs?branch=main&event=push&head_sha={identity.Sha}&per_page=100",
                ct
            );
            var candidates = runs.GetProperty("workflow_runs").EnumerateArray().ToArray();
            if (runs.GetProperty("total_count").GetInt32() != candidates.Length)
                return Result.Fail<WorkflowEvidence[]>("Incomplete workflow inventory.");

            if (candidates.Length == 0)
                continue;

            var runId = candidates.Max(run => run.GetProperty("id").GetInt64());
            var run = await client.GetAsync($"actions/runs/{runId}", ct);
            var identityCheck = ValidateRun(run, expected);
            if (identityCheck.IsFailure)
                return Result.Fail<WorkflowEvidence[]>(identityCheck.Error);

            if (run.GetProperty("status").GetString() != "completed")
                continue;

            if (run.GetProperty("conclusion").GetString() != "success")
                return Result.Fail<WorkflowEvidence[]>("A mandatory workflow did not succeed.");

            var attempt = run.GetProperty("run_attempt").GetInt32();
            var jobs = await client.GetAsync(
                $"actions/runs/{runId}/attempts/{attempt}/jobs?per_page=100",
                ct
            );
            var started = run.GetProperty("run_started_at").GetDateTimeOffset();
            var validated = ValidateJobs(jobs, expected, started);
            if (validated.IsFailure)
                return Result.Fail<WorkflowEvidence[]>(validated.Error);

            evidence.Add(
                new WorkflowEvidence(
                    runId,
                    expected.Id,
                    attempt,
                    expected.Path,
                    identity.Sha,
                    started,
                    expected.SharedPath,
                    expected.SharedSha,
                    validated.Value!
                )
            );
        }

        return Result.Ok(evidence.ToArray());
    }

    private Result ValidateRun(JsonElement run, WorkflowPolicy expected)
    {
        var started = run.GetProperty("run_started_at").GetDateTimeOffset();
        var now = clock.GetUtcNow();
        if (
            !MatchesSource(run, "push", expected.Path)
            || run.GetProperty("workflow_id").GetInt64() != expected.Id
            || started > now
            || now - started >= TimeSpan.FromMinutes(policy.MaximumRunAgeMinutes)
        )
            return Result.Fail("Wrong workflow, source revision or expired CI evidence.");

        if (
            expected.Path == ".github/workflows/ci.yml"
            && (
                run.GetProperty("id").GetInt64() != identity.TriggerRunId
                || run.GetProperty("run_attempt").GetInt32() != identity.TriggerAttempt
            )
        )
            return Result.Fail("The triggering CI attempt was superseded.");

        var referenced = run.GetProperty("referenced_workflows").EnumerateArray().ToArray();
        if (
            referenced.Length != 1
            || referenced[0].GetProperty("path").GetString() != expected.SharedPath
            || referenced[0].GetProperty("sha").GetString() != expected.SharedSha
        )
            return Result.Fail("The shared workflow revision differs from the reviewed policy.");

        return Result.Ok();
    }

    private bool MatchesSource(JsonElement run, string eventName, string path) =>
        run.GetProperty("event").GetString() == eventName
        && run.GetProperty("head_sha").GetString() == identity.Sha
        && run.GetProperty("head_branch").GetString() == "main"
        && run.GetProperty("path").GetString() == path
        && run.GetProperty("repository").GetProperty("full_name").GetString()
            == MainPolicy.Repository
        && run.GetProperty("head_repository").GetProperty("full_name").GetString()
            == MainPolicy.Repository;

    internal static Result<JobEvidence[]> ValidateJobs(
        JsonElement document,
        WorkflowPolicy expected,
        DateTimeOffset attemptStarted
    )
    {
        var jobs = document.GetProperty("jobs").EnumerateArray().ToArray();
        if (document.GetProperty("total_count").GetInt32() != jobs.Length)
            return Result.Fail<JobEvidence[]>("Incomplete job inventory.");

        var evidence = new List<JobEvidence>();
        foreach (var (name, requiredSteps) in expected.Jobs)
        {
            var matches = jobs.Where(job => job.GetProperty("name").GetString() == name).ToArray();
            if (
                matches.Length != 1
                || matches[0].GetProperty("status").GetString() != "completed"
                || matches[0].GetProperty("conclusion").GetString() != "success"
                || matches[0].GetProperty("started_at").GetDateTimeOffset() < attemptStarted
            )
                return Result.Fail<JobEvidence[]>(
                    "A mandatory job is missing, skipped, cancelled or failed."
                );

            var steps = matches[0].GetProperty("steps").EnumerateArray().ToArray();
            foreach (var required in requiredSteps)
            {
                var found = steps
                    .Where(step => step.GetProperty("name").GetString() == required)
                    .ToArray();
                if (
                    found.Length != 1
                    || found[0].GetProperty("status").GetString() != "completed"
                    || found[0].GetProperty("conclusion").GetString() != "success"
                )
                    return Result.Fail<JobEvidence[]>(
                        "A mandatory build, test, audit or drift step did not succeed."
                    );
            }

            evidence.Add(
                new JobEvidence(
                    matches[0].GetProperty("id").GetInt64(),
                    name,
                    "success",
                    matches[0].GetProperty("started_at").GetDateTimeOffset(),
                    requiredSteps
                )
            );
        }

        return Result.Ok(evidence.ToArray());
    }
}
