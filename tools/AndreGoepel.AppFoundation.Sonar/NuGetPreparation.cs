using System.Text.Json;
using AndreGoepel.Core;

namespace AndreGoepel.AppFoundation.Sonar;

internal sealed record NuGetPreparationOutput(
    string ManifestPath,
    string OriginPath,
    string ArtifactName
)
{
    public bool DryRun { get; } = true;
    public bool ReleaseEligible { get; } = false;
}

internal sealed class NuGetPreparation(
    NuGetDryRun dryRun,
    NuGetHandoff handoff,
    IGitHubClient github,
    TimeProvider clock
)
{
    internal async Task<Result<NuGetPreparationOutput>> RunAsync(
        NuGetPreparationContext context,
        CancellationToken ct
    )
    {
        var tag = await NuGetTag.VerifyAsync(github, context.Release, context.TagObjectSha, ct);
        if (tag.IsFailure)
            return Result.Fail<NuGetPreparationOutput>(tag.Error);

        var prepared = await dryRun.RunAsync(context.Release, ct);
        if (prepared.IsFailure)
            return Result.Fail<NuGetPreparationOutput>(prepared.Error);

        var manifestPath = prepared.Value!;
        var directory = Path.GetDirectoryName(manifestPath)!;
        var manifestHash = SourceFiles.Hash(await File.ReadAllBytesAsync(manifestPath, ct));
        var input = new NuGetHandoffInput(
            context.Release,
            directory,
            manifestHash,
            context.TagObjectSha
        );
        var verified = await handoff.VerifyAsync(input, ct);
        if (verified.IsFailure)
            return Result.Fail<NuGetPreparationOutput>(verified.Error);

        var origin = new NuGetPreparationOrigin(
            1,
            MainPolicy.Repository,
            context.Release.Sha,
            context.Release.Tag!,
            context.TagObjectSha,
            context.RunId,
            context.Attempt,
            context.Release.ReadinessRunId,
            manifestHash,
            clock.GetUtcNow(),
            true,
            false
        );
        var originDirectory = Path.Combine(directory, "origin");
        if (Directory.Exists(originDirectory) || File.Exists(originDirectory))
            return Result.Fail<NuGetPreparationOutput>(
                "The origin output directory must not already exist."
            );
        Directory.CreateDirectory(originDirectory);
        var originPath = Path.Combine(originDirectory, "origin.json");
        var pending = Path.Combine(originDirectory, "origin.pending");
        await using (
            var stream = new FileStream(
                pending,
                FileMode.CreateNew,
                FileAccess.Write,
                FileShare.None,
                4096,
                useAsync: true
            )
        )
            await JsonSerializer.SerializeAsync(stream, origin, JsonFormat.Options, ct);
        ct.ThrowIfCancellationRequested();
        File.Move(pending, originPath);

        return Result.Ok(
            new NuGetPreparationOutput(
                manifestPath,
                originPath,
                $"nuget-origin-{context.Release.Sha}-{context.RunId}-{context.Attempt}"
            )
        );
    }
}
