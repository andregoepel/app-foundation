using System.Globalization;
using AndreGoepel.Core;

namespace AndreGoepel.AppFoundation.Sonar;

internal sealed record NuGetHandoffOptions(
    NuGetHandoffInput Input,
    long PreparationRunId,
    int PreparationAttempt,
    string[]? Files,
    bool VerifyOnly = false,
    bool VerifyStoredIntent = false,
    NuGetIntentContext? Inspection = null
)
{
    internal const string Usage =
        "Use --root ROOT MODE --sha SHA --version X.Y.Z --readiness-run ID --tag TAG --handoff DIRECTORY --manifest-sha256 HASH --tag-object SHA --preparation-run ID --preparation-attempt NUMBER. MODE is --nuget-verify-handoff, --nuget-verify-intent or --nuget-rehearse; only rehearsal accepts [--files FILE ...]. TAG must be vX.Y.Z or sonar-poc/nuget/LOWERCASE_HEX_32/vX.Y.Z; intent verification requires a production tag and the current publishing job context. All modes are non-publishing.";

    internal static Result<NuGetHandoffOptions> Parse(string[] args)
    {
        if (args.Length > 2 && args[2] == "--nuget-inspect-publication")
            return ParseInspection(args);

        if (
            args
                is not [
                    "--root",
                    var root,
                    var mode,
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
            || mode
                is not ("--nuget-rehearse" or "--nuget-verify-handoff" or "--nuget-verify-intent")
            || (mode != "--nuget-rehearse" && tail.Length != 0)
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
            return Result.Fail<NuGetHandoffOptions>(Usage);

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
            return Result.Fail<NuGetHandoffOptions>(Usage);

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
            return Result.Fail<NuGetHandoffOptions>(Usage);

        return Result.Ok(
            new NuGetHandoffOptions(
                new NuGetHandoffInput(
                    release.Value!,
                    Path.GetFullPath(directory),
                    digest,
                    tagObject
                ),
                runId,
                attemptId,
                files,
                mode != "--nuget-rehearse",
                mode == "--nuget-verify-intent"
            )
        );
    }

    private static bool IsHex(string value, int length) =>
        value.Length == length && value.All(c => c is >= '0' and <= '9' or >= 'a' and <= 'f');

    private static Result<NuGetHandoffOptions> ParseInspection(string[] args)
    {
        const string usage =
            "Use the handoff arguments with --nuget-inspect-publication followed by --publish-run ID --intent-artifact ID --intent-archive-sha256 HASH --intent-sha256 HASH. This only inspects records; it never authorizes retry.";
        if (
            args.Length != 29
            || args[21..]
                is not [
                    "--publish-run",
                    var run,
                    "--intent-artifact",
                    var artifact,
                    "--intent-archive-sha256",
                    var archive,
                    "--intent-sha256",
                    var content,
                ]
            || !long.TryParse(run, NumberStyles.None, CultureInfo.InvariantCulture, out var runId)
            || !long.TryParse(
                artifact,
                NumberStyles.None,
                CultureInfo.InvariantCulture,
                out var artifactId
            )
            || runId <= 0
            || artifactId <= 0
            || !IsHex(archive, 64)
            || !IsHex(content, 64)
        )
            return Result.Fail<NuGetHandoffOptions>(usage);

        var handoffArgs = args[..21];
        handoffArgs[2] = "--nuget-verify-handoff";
        var handoff = Parse(handoffArgs);
        if (
            handoff.IsFailure
            || handoff.Value!.PreparationAttempt != 1
            || handoff.Value.Input.Release.Tag != "v" + handoff.Value.Input.Release.Version
        )
            return Result.Fail<NuGetHandoffOptions>(usage);

        return Result.Ok(
            handoff.Value with
            {
                Inspection = new(runId, artifactId, "sha256:" + archive, content),
            }
        );
    }
}
