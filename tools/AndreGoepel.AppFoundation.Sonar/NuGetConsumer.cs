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
        NuGetHandoffOptions options,
        CancellationToken ct
    )
    {
        var handoff = CreateHandoff(options);
        return new NuGetPublicationRehearsal(handoff, new NuGetDiscardSink()).RunAsync(
            options.Input,
            options.Files,
            ct
        );
    }

    internal Task<Result<NuGetHandoffReceipt>> VerifyAsync(
        NuGetHandoffOptions options,
        CancellationToken ct
    ) => CreateHandoff(options).VerifyAsync(options.Input, ct);

    private NuGetPreparedHandoff CreateHandoff(NuGetHandoffOptions options) =>
        new(
            new NuGetProvenance(github, artifacts, policy, clock),
            new NuGetHandoff(readiness, github, clock),
            options.PreparationRunId,
            options.PreparationAttempt
        );
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
