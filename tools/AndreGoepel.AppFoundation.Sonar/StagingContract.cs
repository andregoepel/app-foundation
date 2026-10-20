namespace AndreGoepel.AppFoundation.Sonar;

internal sealed record StagingCandidate(
    int SchemaVersion,
    string Repository,
    string Sha,
    long ReadinessRunId,
    string AnalysisId,
    string SourceSha256,
    long BuildRunId,
    int BuildAttempt,
    string Image,
    string Target,
    long OperatorGeneration
);

internal sealed record StagingBuildEvidence(
    string Repository,
    string HeadRepository,
    string Sha,
    long RunId,
    int Attempt,
    long Sequence,
    long WorkflowId,
    string WorkflowPath,
    string WorkflowRevision,
    string WorkflowSha256,
    string Event,
    string Branch,
    string Conclusion,
    long ReadinessRunId,
    string AnalysisId,
    string SourceSha256,
    long ArtifactId,
    string ArtifactSha256,
    string Image,
    string RegistryImage,
    DateTimeOffset StartedAt,
    DateTimeOffset ExpiresAt
);

internal sealed record StagingOperatorState(
    string Target,
    string Entrypoint,
    long Generation,
    long LastBuildSequence,
    bool Busy,
    bool RecoveryPending,
    bool ProductionSelected
);

internal sealed record StagingEvidence(StagingBuildEvidence Build, StagingOperatorState Operator);

internal sealed record StagingContractPolicy(
    long WorkflowId,
    string WorkflowPath,
    string WorkflowSha256,
    string ImageRepository,
    string OperatorEntrypoint
);

internal sealed record StagingContractReceipt(
    string Sha,
    long ReadinessRunId,
    string AnalysisId,
    string Image,
    long BuildRunId,
    long OperatorGeneration,
    DateTimeOffset ExpiresAt
)
{
    public int SchemaVersion { get; } = 1;
    public bool ContractAccepted { get; } = true;
    public bool SyntheticDeploymentEvidence { get; } = true;
    public bool DeploymentEligible { get; } = false;
    public bool DryRun { get; } = true;
}
