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

    internal async Task<Result<NuGetHandoffReceipt>> VerifyIntentAsync(
        NuGetHandoffOptions options,
        NuGetIntentContext pins,
        NuGetHandoffReceipt verified,
        CancellationToken ct
    )
    {
        var intent = await new NuGetStoredIntent(github, artifacts, clock).VerifyAsync(
            options,
            pins,
            verified,
            ct
        );
        if (intent.IsFailure)
            return Result.Fail<NuGetHandoffReceipt>(intent.Error);

        var final = await VerifyAsync(options, ct);
        return final.IsSuccess && !verified.Packages.SequenceEqual(final.Value!.Packages)
            ? Result.Fail<NuGetHandoffReceipt>("The handoff changed during intent verification.")
            : final;
    }

    internal async Task<Result<NuGetPublicationInspection>> InspectAsync(
        NuGetHandoffOptions options,
        NuGetIntentContext pins,
        NuGetHandoffReceipt verified,
        CancellationToken ct
    )
    {
        var result = await new NuGetPublicationRecords(github, artifacts, clock).InspectAsync(
            options,
            pins,
            verified,
            ct
        );
        if (result.IsFailure)
            return result;

        var final = await VerifyAsync(options, ct);
        return final.IsSuccess && verified.Packages.SequenceEqual(final.Value!.Packages)
            ? result
            : Result.Fail<NuGetPublicationInspection>(
                "The handoff changed or expired during inspection."
            );
    }

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
