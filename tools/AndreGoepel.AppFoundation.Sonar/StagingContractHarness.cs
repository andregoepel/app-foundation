using System.Text.Json;
using AndreGoepel.Core;

namespace AndreGoepel.AppFoundation.Sonar;

internal sealed record StagingContractCase(
    string Name,
    bool ExpectedAcceptance,
    bool Accepted,
    string? Reason
);

internal sealed record StagingContractReport(
    string Sha,
    long ReadinessRunId,
    string? AnalysisId,
    bool ActualReadinessAccepted,
    StagingContractCase[] Cases,
    string? ReadinessError
)
{
    public int SchemaVersion { get; } = 1;
    public bool SyntheticDeploymentEvidence { get; } = true;
    public bool DeploymentEligible { get; } = false;
    public bool DryRun { get; } = true;
    public bool Passed =>
        ActualReadinessAccepted
        && Cases.Length > 0
        && Cases.All(c => c.Accepted == c.ExpectedAcceptance);
}

internal sealed class StagingContractHarness(IReleaseReadiness readiness, TimeProvider clock)
{
    internal async Task<StagingContractReport> RunAsync(
        ReleaseCandidate release,
        CancellationToken ct
    )
    {
        var ready = await readiness.VerifyAsync(release, ct);
        if (ready.IsFailure)
            return new(release.Sha, release.ReadinessRunId, null, false, [], ready.Error);

        var cases = new List<StagingContractCase>();
        foreach (var name in StagingSyntheticEvidence.Scenarios)
        {
            var fixture = new StagingSyntheticEvidence(ready.Value!, clock.GetUtcNow());
            var candidate = fixture.Candidate;
            var snapshot = fixture.Apply(name, ref candidate);
            var verifier = new StagingEligibility(
                snapshot,
                fixture,
                StagingSyntheticEvidence.Policy,
                clock
            );
            var result = await verifier.VerifyAsync(release, candidate, ct);
            cases.Add(
                new(name, name == "valid", result.IsSuccess, result.IsFailure ? result.Error : null)
            );
        }

        var final = await readiness.VerifyAsync(release, ct);
        var unchanged =
            final.IsSuccess
            && JsonSerializer.Serialize(ready.Value, JsonFormat.Options)
                == JsonSerializer.Serialize(final.Value, JsonFormat.Options);
        return new(
            release.Sha,
            release.ReadinessRunId,
            ready.Value!.AnalysisId,
            unchanged,
            cases.ToArray(),
            unchanged ? null : "Actual readiness changed or expired during the contract harness."
        );
    }
}

internal sealed class StagingSyntheticEvidence : IStagingEvidenceSource
{
    internal static readonly StagingContractPolicy Policy = new(
        900000001,
        "synthetic/image-build.yml",
        SourceFiles.Hash("synthetic reviewed workflow"u8.ToArray()),
        "ghcr.io/andregoepel/app-foundation-contract-fixture",
        "/synthetic/operator/deploy-staging"
    );

    internal static readonly string[] Scenarios =
    [
        "valid",
        "wrong-sha",
        "wrong-digest",
        "mutable-image",
        "unknown-workflow",
        "wrong-workflow-revision",
        "wrong-workflow-hash",
        "fork",
        "failed-build",
        "build-rerun",
        "expired-build",
        "missing-e2e",
        "absent-gate",
        "failed-gate",
        "expired-readiness",
        "reordered",
        "busy",
        "recovery",
        "production",
        "changed-generation",
        "arbitrary-entrypoint",
        "evidence-changed",
        "missing-provenance",
    ];

    private readonly ReadinessReceipt ready;
    internal StagingCandidate Candidate { get; }
    internal StagingEvidence Evidence { get; set; }
    internal Action? OnRead { get; set; }

    internal StagingSyntheticEvidence(ReadinessReceipt ready, DateTimeOffset now)
    {
        this.ready = ready;
        var image =
            Policy.ImageRepository
            + "@sha256:"
            + SourceFiles.Hash("synthetic image bytes"u8.ToArray());
        Candidate = new(
            1,
            MainPolicy.Repository,
            ready.Run.Sha,
            ready.Run.RunId,
            ready.AnalysisId,
            ready.SourceSha256,
            900000002,
            1,
            image,
            "staging",
            4
        );
        Evidence = new(
            new(
                MainPolicy.Repository,
                MainPolicy.Repository,
                ready.Run.Sha,
                Candidate.BuildRunId,
                1,
                5,
                Policy.WorkflowId,
                Policy.WorkflowPath,
                ready.Run.Sha,
                Policy.WorkflowSha256,
                "push",
                "main",
                "success",
                ready.Run.RunId,
                ready.AnalysisId,
                ready.SourceSha256,
                900000003,
                SourceFiles.Hash("synthetic provenance archive"u8.ToArray()),
                image,
                image,
                now,
                ready.ExpiresAt
            ),
            new("staging", Policy.OperatorEntrypoint, 4, 4, false, false, false)
        );
    }

    internal IReleaseReadiness Apply(string name, ref StagingCandidate candidate)
    {
        var build = Evidence.Build;
        var state = Evidence.Operator;
        switch (name)
        {
            case "wrong-sha":
                candidate = candidate with { Sha = new string('f', 40) };
                break;
            case "wrong-digest":
                candidate = candidate with
                {
                    Image = Policy.ImageRepository + "@sha256:" + new string('f', 64),
                };
                break;
            case "mutable-image":
                candidate = candidate with { Image = Policy.ImageRepository + ":latest" };
                break;
            case "unknown-workflow":
                build = build with { WorkflowId = 123 };
                break;
            case "wrong-workflow-revision":
                build = build with { WorkflowRevision = new string('f', 40) };
                break;
            case "wrong-workflow-hash":
                build = build with { WorkflowSha256 = new string('f', 64) };
                break;
            case "fork":
                build = build with { HeadRepository = "someone/fork" };
                break;
            case "failed-build":
                build = build with { Conclusion = "failure" };
                break;
            case "build-rerun":
                build = build with { Attempt = 2 };
                break;
            case "expired-build":
                build = build with { ExpiresAt = ready.IssuedAt };
                break;
            case "reordered":
                state = state with { LastBuildSequence = build.Sequence };
                break;
            case "busy":
                state = state with { Busy = true };
                break;
            case "recovery":
                state = state with { RecoveryPending = true };
                break;
            case "production":
                candidate = candidate with { Target = "production" };
                break;
            case "changed-generation":
                state = state with { Generation = 5 };
                break;
            case "arbitrary-entrypoint":
                state = state with { Entrypoint = "/untrusted/command" };
                break;
            case "missing-provenance":
                build = build with { ArtifactId = 0 };
                break;
            case "evidence-changed":
                OnRead = () =>
                    Evidence = Evidence with
                    {
                        Operator = Evidence.Operator with { Generation = 5 },
                    };
                break;
        }
        Evidence = new(build, state);
        return new SnapshotReadiness(
            ready,
            name is "missing-e2e" or "absent-gate" or "failed-gate" or "expired-readiness"
                ? "Synthetic readiness rejection: " + name
                : null
        );
    }

    public Task<Result<StagingEvidence>> ReadAsync(long buildRunId, CancellationToken ct)
    {
        ct.ThrowIfCancellationRequested();
        var result =
            buildRunId == Evidence.Build.RunId
                ? Result.Ok(Evidence)
                : Result.Fail<StagingEvidence>("Synthetic build not found.");
        OnRead?.Invoke();
        return Task.FromResult(result);
    }

    private sealed class SnapshotReadiness(ReadinessReceipt ready, string? failure)
        : IReleaseReadiness
    {
        public Task<Result<ReadinessReceipt>> VerifyAsync(
            ReleaseCandidate release,
            CancellationToken ct
        )
        {
            ct.ThrowIfCancellationRequested();
            return Task.FromResult(
                failure is null ? Result.Ok(ready) : Result.Fail<ReadinessReceipt>(failure)
            );
        }
    }
}
