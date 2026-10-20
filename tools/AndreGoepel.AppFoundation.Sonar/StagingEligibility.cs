using System.Text.Json;
using AndreGoepel.Core;

namespace AndreGoepel.AppFoundation.Sonar;

internal interface IStagingEvidenceSource
{
    Task<Result<StagingEvidence>> ReadAsync(long buildRunId, CancellationToken ct);
}

internal sealed class StagingEligibility(
    IReleaseReadiness readiness,
    IStagingEvidenceSource evidence,
    StagingContractPolicy policy,
    TimeProvider clock
)
{
    internal async Task<Result<StagingContractReceipt>> VerifyAsync(
        ReleaseCandidate release,
        StagingCandidate candidate,
        CancellationToken ct
    )
    {
        var ready = await readiness.VerifyAsync(release, ct);
        if (ready.IsFailure)
            return Result.Fail<StagingContractReceipt>(ready.Error);
        if (!MatchesReadiness(release, candidate, ready.Value!))
            return Result.Fail<StagingContractReceipt>(
                "The staging candidate does not bind to current main readiness."
            );

        var initial = await evidence.ReadAsync(candidate.BuildRunId, ct);
        if (initial.IsFailure)
            return Result.Fail<StagingContractReceipt>(initial.Error);
        var checkedEvidence = CheckEvidence(candidate, initial.Value!, ready.Value!);
        if (checkedEvidence.IsFailure)
            return Result.Fail<StagingContractReceipt>(checkedEvidence.Error);

        var finalReady = await readiness.VerifyAsync(release, ct);
        var finalEvidence = await evidence.ReadAsync(candidate.BuildRunId, ct);
        if (
            finalReady.IsFailure
            || finalEvidence.IsFailure
            || JsonSerializer.Serialize(ready.Value, JsonFormat.Options)
                != JsonSerializer.Serialize(finalReady.Value, JsonFormat.Options)
            || initial.Value != finalEvidence.Value
            || CheckEvidence(candidate, finalEvidence.Value!, finalReady.Value!).IsFailure
        )
            return Result.Fail<StagingContractReceipt>(
                "Staging evidence or operator state changed during verification."
            );

        return Result.Ok(
            new StagingContractReceipt(
                release.Sha,
                release.ReadinessRunId,
                ready.Value!.AnalysisId,
                candidate.Image,
                candidate.BuildRunId,
                candidate.OperatorGeneration,
                new[] { ready.Value.ExpiresAt, initial.Value!.Build.ExpiresAt }.Min()
            )
        );
    }

    private bool MatchesReadiness(
        ReleaseCandidate release,
        StagingCandidate candidate,
        ReadinessReceipt ready
    ) =>
        candidate.SchemaVersion == 1
        && release.Tag is null
        && candidate.Repository == MainPolicy.Repository
        && candidate.Repository == ready.Repository
        && candidate.Sha == release.Sha
        && candidate.Sha == ready.Run.Sha
        && candidate.ReadinessRunId == release.ReadinessRunId
        && candidate.ReadinessRunId == ready.Run.RunId
        && candidate.AnalysisId == ready.AnalysisId
        && candidate.SourceSha256 == ready.SourceSha256
        && candidate.Target == "staging"
        && ready.ExpiresAt > clock.GetUtcNow();

    private Result CheckEvidence(
        StagingCandidate candidate,
        StagingEvidence observed,
        ReadinessReceipt ready
    )
    {
        var build = observed.Build;
        if (
            build.Repository != MainPolicy.Repository
            || build.HeadRepository != MainPolicy.Repository
            || build.Sha != candidate.Sha
            || build.RunId != candidate.BuildRunId
            || build.RunId <= 0
            || build.Attempt != 1
            || candidate.BuildAttempt != 1
            || build.WorkflowId != policy.WorkflowId
            || build.WorkflowPath != policy.WorkflowPath
            || build.WorkflowSha256 != policy.WorkflowSha256
            || build.WorkflowRevision != candidate.Sha
            || build.Event != "push"
            || build.Branch != "main"
            || build.Conclusion != "success"
            || build.ReadinessRunId != candidate.ReadinessRunId
            || build.AnalysisId != ready.AnalysisId
            || build.SourceSha256 != ready.SourceSha256
            || build.ArtifactId <= 0
            || !IsHash(build.ArtifactSha256)
            || build.Image != candidate.Image
            || build.RegistryImage != candidate.Image
            || !candidate.Image.StartsWith(
                policy.ImageRepository + "@sha256:",
                StringComparison.Ordinal
            )
            || !IsHash(candidate.Image[(policy.ImageRepository.Length + "@sha256:".Length)..])
            || build.StartedAt < ready.IssuedAt
            || build.StartedAt > clock.GetUtcNow()
            || build.ExpiresAt <= clock.GetUtcNow()
            || build.ExpiresAt > ready.ExpiresAt
        )
            return Result.Fail(
                "The independently observed image build, digest or lifetime does not match the candidate."
            );

        var state = observed.Operator;
        return
            state.Target == "staging"
            && state.Entrypoint == policy.OperatorEntrypoint
            && state.Generation >= 0
            && state.Generation == candidate.OperatorGeneration
            && state.LastBuildSequence >= 0
            && build.Sequence > state.LastBuildSequence
            && !state.Busy
            && !state.RecoveryPending
            && !state.ProductionSelected
            ? Result.Ok()
            : Result.Fail(
                "The operator boundary is busy, changed, recovering, reordered or selects production."
            );
    }

    private static bool IsHash(string value) =>
        value.Length == 64 && value.All(c => c is >= '0' and <= '9' or >= 'a' and <= 'f');
}
