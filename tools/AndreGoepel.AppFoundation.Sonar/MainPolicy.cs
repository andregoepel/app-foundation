using System.Text.Json;

namespace AndreGoepel.AppFoundation.Sonar;

internal sealed record GateCondition(string Metric, string Op, string Error);

internal sealed record ProfilePolicy(
    string Language,
    string Name,
    int ActiveRuleCount,
    DateTimeOffset RulesUpdatedAt
);

internal sealed record WorkflowPolicy(
    long Id,
    string Path,
    string FileSha256,
    string SharedPath,
    string SharedSha,
    Dictionary<string, string[]> Jobs
);

internal sealed record MainPolicy(
    string Version,
    string? BaselineAnalysisId,
    int MaximumRunAgeMinutes,
    int EvidenceLifetimeMinutes,
    GateCondition[] Conditions,
    ProfilePolicy[] Profiles,
    WorkflowPolicy[] Workflows
)
{
    internal const string Repository = "andregoepel/app-foundation";
    internal const string WorkflowPath = ".github/workflows/sonar-main.yml";
    internal const string SonarUrl = "https://sonar.andregoepel.dev/";
    internal const string Project = "app-foundation";
    internal const string ServerId = "AA2DD71C-AaFDycIuQLOXJXX6YWNZ";

    internal string Fingerprint() =>
        SourceFiles.Hash(JsonSerializer.SerializeToUtf8Bytes(this, JsonFormat.Options));
}

internal sealed record MainRunIdentity(
    string Sha,
    long RunId,
    int Attempt,
    long TriggerRunId,
    int TriggerAttempt
)
{
    internal string RunName => $"main-{RunId}-{Attempt}";
    internal string BuildString => $"github:{MainPolicy.Repository}:{RunId}:{Attempt}:{Sha}";
}

internal sealed record JobEvidence(
    long Id,
    string Name,
    string Conclusion,
    DateTimeOffset StartedAt,
    string[] Steps
);

internal sealed record WorkflowEvidence(
    long Id,
    long WorkflowId,
    int Attempt,
    string Path,
    string Sha,
    DateTimeOffset StartedAt,
    string SharedPath,
    string SharedSha,
    JobEvidence[] Jobs
);

internal sealed record ReadinessReceipt(
    int SchemaVersion,
    string Repository,
    MainRunIdentity Run,
    string WorkflowPath,
    string WorkflowRevision,
    string PolicyVersion,
    string PolicySha256,
    string AnalysisContractSha256,
    string AnalysisReceiptSha256,
    string SourceSha256,
    string BaselineAnalysisId,
    string TaskId,
    string AnalysisId,
    WorkflowEvidence[] MandatoryWorkflows,
    DateTimeOffset IssuedAt,
    DateTimeOffset ExpiresAt
)
{
    public bool ReleaseEligible { get; } = true;
}
