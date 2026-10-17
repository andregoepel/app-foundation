using System.Text.Json;
using AndreGoepel.Core;

namespace AndreGoepel.AppFoundation.Sonar;

internal sealed class NuGetDryRun(IReleaseReadiness readiness, INuGetPackageBuilder packages)
{
    internal async Task<Result<string>> RunAsync(NuGetRelease release, CancellationToken ct)
    {
        var initial = await readiness.VerifyAsync(release, ct);
        if (initial.IsFailure)
            return Result.Fail<string>(initial.Error);

        var directory = Path.Combine(
            release.Root,
            "artifacts/sonar/nuget-" + Guid.NewGuid().ToString("N")
        );
        Directory.CreateDirectory(directory);
        var built = await packages.BuildAsync(release, directory, ct);
        if (built.IsFailure)
            return Result.Fail<string>(built.Error);

        var final = await readiness.VerifyAsync(release, ct);
        if (
            final.IsFailure
            || JsonSerializer.Serialize(initial.Value, JsonFormat.Options)
                != JsonSerializer.Serialize(final.Value, JsonFormat.Options)
        )
            return Result.Fail<string>(
                "Readiness changed while preparing packages; revalidate main."
            );

        foreach (var package in built.Value!)
        {
            var bytes = await File.ReadAllBytesAsync(
                Path.Combine(directory, "packages", package.File),
                ct
            );
            if (SourceFiles.Hash(bytes) != package.Sha256)
                return Result.Fail<string>("Prepared packages changed during final verification.");
        }

        var receipt = new NuGetDryRunReceipt(
            release.Sha,
            release.Version,
            release.ReadinessRunId,
            final.Value!.Run.Attempt,
            final.Value.ExpiresAt,
            built.Value!
        );
        var path = Path.Combine(directory, "dry-run.json");
        await File.WriteAllTextAsync(
            path,
            JsonSerializer.Serialize(receipt, JsonFormat.Options),
            ct
        );

        return Result.Ok(path);
    }
}
