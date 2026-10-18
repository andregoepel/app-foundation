using System.Security.Cryptography;
using AndreGoepel.Core;

namespace AndreGoepel.AppFoundation.Sonar;

internal sealed class NuGetConsumer(
    IReleaseReadiness readiness,
    IGitHubClient github,
    IGitHubArtifacts artifacts,
    NuGetPreparationPolicy policy,
    TimeProvider clock
)
{
    internal Task<Result<NuGetRehearsalReceipt>> RunAsync(
        NuGetRehearsalOptions options,
        CancellationToken ct
    )
    {
        var handoff = new NuGetPreparedHandoff(
            new NuGetProvenance(github, artifacts, policy, clock),
            new NuGetHandoff(readiness, github, clock),
            options.PreparationRunId,
            options.PreparationAttempt
        );
        return new NuGetPublicationRehearsal(handoff, new NuGetDiscardSink()).RunAsync(
            options.Input,
            options.Files,
            ct
        );
    }
}

internal sealed class NuGetDiscardSink : INuGetRehearsalSink
{
    private bool loggedIn;

    public Task<Result> SimulateLoginAsync(CancellationToken ct)
    {
        ct.ThrowIfCancellationRequested();
        loggedIn = true;
        return Task.FromResult(Result.Ok());
    }

    public async Task<Result> SimulateUploadAsync(
        PackageEvidence package,
        Stream bytes,
        CancellationToken ct
    )
    {
        ct.ThrowIfCancellationRequested();
        if (!loggedIn || package.Feed != NuGetRelease.Feed)
            return Result.Fail(
                "The discard sink requires a simulated login and the approved feed."
            );

        var digest = Convert.ToHexStringLower(await SHA256.HashDataAsync(bytes, ct));
        return digest == package.Sha256
            ? Result.Ok()
            : Result.Fail("The discard sink received different package bytes.");
    }
}
