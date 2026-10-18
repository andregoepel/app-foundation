using System.Globalization;
using AndreGoepel.Core;

namespace AndreGoepel.AppFoundation.Sonar;

internal sealed record NuGetRehearsalOptions(
    NuGetHandoffInput Input,
    long PreparationRunId,
    int PreparationAttempt,
    string[]? Files
)
{
    internal const string Usage =
        "Use --root ROOT --nuget-rehearse --sha SHA --version X.Y.Z --readiness-run ID --tag vX.Y.Z --handoff DIRECTORY --manifest-sha256 HASH --tag-object SHA --preparation-run ID --preparation-attempt NUMBER [--files FILE ...].";

    internal static Result<NuGetRehearsalOptions> Parse(string[] args)
    {
        if (
            args
                is not [
                    "--root",
                    var root,
                    "--nuget-rehearse",
                    "--sha",
                    var sha,
                    "--version",
                    var version,
                    "--readiness-run",
                    var readiness,
                    "--tag",
                    var tag,
                    "--handoff",
                    var directory,
                    "--manifest-sha256",
                    var digest,
                    "--tag-object",
                    var tagObject,
                    "--preparation-run",
                    var run,
                    "--preparation-attempt",
                    var attempt,
                    .. var tail,
                ]
            || !Path.IsPathFullyQualified(directory)
            || !Directory.Exists(directory)
            || !IsHex(digest, 64)
            || !IsHex(tagObject, 40)
            || !long.TryParse(run, NumberStyles.None, CultureInfo.InvariantCulture, out var runId)
            || runId <= 0
            || !int.TryParse(
                attempt,
                NumberStyles.None,
                CultureInfo.InvariantCulture,
                out var attemptId
            )
            || attemptId <= 0
            || (tail.Length != 0 && tail is not ["--files", _, ..])
        )
            return Result.Fail<NuGetRehearsalOptions>(Usage);

        var release = NuGetRelease.Parse([
            "--root",
            root,
            "--nuget-prepare",
            "--sha",
            sha,
            "--version",
            version,
            "--readiness-run",
            readiness,
            "--tag",
            tag,
        ]);
        if (release.IsFailure)
            return Result.Fail<NuGetRehearsalOptions>(Usage);

        var files = tail.Length == 0 ? null : tail[1..];
        var inventory = NuGetRelease.Packages.SelectMany(id =>
            new[] { $"{id}.{version}.nupkg", $"{id}.{version}.snupkg" }
        );
        if (
            files is not null
            && (
                files.Distinct(StringComparer.Ordinal).Count() != files.Length
                || files.Except(inventory, StringComparer.Ordinal).Any()
            )
        )
            return Result.Fail<NuGetRehearsalOptions>(Usage);

        return Result.Ok(
            new NuGetRehearsalOptions(
                new NuGetHandoffInput(
                    release.Value!,
                    Path.GetFullPath(directory),
                    digest,
                    tagObject
                ),
                runId,
                attemptId,
                files
            )
        );
    }

    private static bool IsHex(string value, int length) =>
        value.Length == length && value.All(c => c is >= '0' and <= '9' or >= 'a' and <= 'f');
}
