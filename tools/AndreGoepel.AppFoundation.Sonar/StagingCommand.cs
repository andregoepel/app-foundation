using System.Globalization;
using System.Text.Json;
using AndreGoepel.Core;

namespace AndreGoepel.AppFoundation.Sonar;

internal static class StagingCommand
{
    internal static async Task<Result> RunAsync(string[] args, CancellationToken ct)
    {
        var boundary = NuGetRelease.CheckCredentialBoundary(Environment.GetEnvironmentVariable);
        if (boundary.IsFailure)
            return boundary;
        var parsed = Parse(args);
        if (parsed.IsFailure)
            return parsed;
        var release = parsed.Value!;
        var opened = await ReleaseVerificationSession.OpenAsync(release.Root, ct);
        if (opened.IsFailure)
            return opened;
        using var session = opened.Value!;

        var report = await new StagingContractHarness(
            session.Readiness,
            TimeProvider.System
        ).RunAsync(release, ct);
        Console.WriteLine(JsonSerializer.Serialize(report, JsonFormat.Options));
        return report.Passed
            ? Result.Ok()
            : Result.Fail("The non-deploying staging contract harness did not pass.");
    }

    internal static Result<ReleaseCandidate> Parse(string[] args)
    {
        if (
            args
                is not [
                    "--root",
                    var root,
                    "--staging-contract",
                    "--sha",
                    var sha,
                    "--readiness-run",
                    var run,
                ]
            || !Directory.Exists(root)
            || sha.Length != 40
            || sha.Any(c => c is not (>= '0' and <= '9' or >= 'a' and <= 'f'))
            || !long.TryParse(run, NumberStyles.None, CultureInfo.InvariantCulture, out var id)
            || id <= 0
        )
            return Result.Fail<ReleaseCandidate>(
                "Use --root ROOT --staging-contract --sha SHA --readiness-run ID. This command cannot deploy."
            );
        return Result.Ok(new ReleaseCandidate(Path.GetFullPath(root), sha, id));
    }
}
