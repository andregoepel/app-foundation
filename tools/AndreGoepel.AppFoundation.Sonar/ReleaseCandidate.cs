namespace AndreGoepel.AppFoundation.Sonar;

internal record ReleaseCandidate(string Root, string Sha, long ReadinessRunId, string? Tag = null)
{
    internal const long ReadinessWorkflowId = 387320916;
}
