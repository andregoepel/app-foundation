using System.Text.Json;
using AndreGoepel.Core;

namespace AndreGoepel.AppFoundation.Sonar;

internal interface IAnalysisTrust
{
    string Revision { get; }
    string BuildString { get; }
    Result ValidateTarget(AnalysisOptions options);
    Task<Result> CheckAsync(CancellationToken cancellationToken);
    Task<Result> VerifyAsync(
        AnalysisSummary summary,
        JsonElement latest,
        CancellationToken cancellationToken
    );
}

internal sealed class MainAnalysisTrust(
    MainRunIdentity identity,
    GitHubEvidence github,
    WorkflowEvidence[] initialEvidence,
    MainSonarPolicy sonar
) : IAnalysisTrust
{
    public string Revision => identity.Sha;
    public string BuildString => identity.BuildString;

    public Result ValidateTarget(AnalysisOptions options) =>
        options.Url.AbsoluteUri == MainPolicy.SonarUrl
        && options.Project == MainPolicy.Project
        && options.ServerId == MainPolicy.ServerId
        && options.RunName == identity.RunName
            ? Result.Ok()
            : Result.Fail("Unexpected central analysis target.");

    public async Task<Result> CheckAsync(CancellationToken cancellationToken)
    {
        var current = await github.ReadAsync(cancellationToken);
        if (current.IsFailure)
            return current;

        if (
            JsonSerializer.Serialize(current.Value, JsonFormat.Options)
            != JsonSerializer.Serialize(initialEvidence, JsonFormat.Options)
        )
            return Result.Fail(
                "Mandatory CI evidence changed or became incomplete during analysis."
            );

        return await sonar.CheckDefinitionAsync(cancellationToken);
    }

    public async Task<Result> VerifyAsync(
        AnalysisSummary summary,
        JsonElement latest,
        CancellationToken cancellationToken
    )
    {
        var definition = await CheckAsync(cancellationToken);
        if (definition.IsFailure)
            return definition;

        return await sonar.VerifyAnalysisAsync(summary, latest, identity, cancellationToken);
    }
}
