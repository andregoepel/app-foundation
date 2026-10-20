using System.Text.Json;
using AndreGoepel.Core;

namespace AndreGoepel.AppFoundation.Sonar;

internal sealed record NuGetPublicationSteps(string Login, string Push);

internal sealed record NuGetFileObservation(PackageEvidence Package, string CliObservation)
{
    public bool RemoteAcceptanceVerified { get; } = false;
}

internal sealed record NuGetPublicationInspection(
    long RunId,
    bool OutcomesAvailable,
    NuGetFileObservation[] Packages
)
{
    public bool AcceptanceOnly { get; init; }
    public int SchemaVersion { get; } = 1;
    public bool AuthorizesRetry { get; } = false;
    public bool ReleaseEligible { get; } = false;
    public bool RemoteAcceptanceVerified { get; } = false;
}

internal static class NuGetPublicationOutcome
{
    private static readonly string[] StepNames =
    [
        "Preserve publication intent before login",
        "Reverify after preserving intent",
        "Obtain temporary NuGet credentials",
        "Push each reverified package and symbol file",
    ];

    internal static Result<NuGetPublicationSteps> ReadSteps(JsonElement document)
    {
        var jobs = document.GetProperty("jobs").EnumerateArray().ToArray();
        if (document.GetProperty("total_count").GetInt32() != 1 || jobs.Length != 1)
            return Result.Fail<NuGetPublicationSteps>("Publication job inventory is incomplete.");

        var job = jobs[0];
        var steps = job.GetProperty("steps").EnumerateArray().ToArray();
        var matches = StepNames
            .Select(name => steps.Where(s => s.GetProperty("name").GetString() == name).ToArray())
            .ToArray();
        if (
            job.GetProperty("name").GetString() != "publish"
            || job.GetProperty("status").GetString() != "completed"
            || matches.Any(s => s.Length != 1)
        )
            return Result.Fail<NuGetPublicationSteps>(
                "Publication steps are missing or ambiguous."
            );

        var selected = matches.Select(s => s[0]).ToArray();
        var numbers = selected.Select(s => s.GetProperty("number").GetInt32()).ToArray();
        var conclusions = selected.Select(s => s.GetProperty("conclusion").GetString()).ToArray();
        if (
            numbers.Distinct().Count() != numbers.Length
            || !numbers.SequenceEqual(numbers.Order())
            || selected.Any(s => s.GetProperty("status").GetString() != "completed")
            || conclusions[0] != "success"
            || conclusions
                .Skip(1)
                .Any(c => c is not ("success" or "failure" or "cancelled" or "skipped"))
            || (conclusions[1] != "success" && conclusions[2] != "skipped")
            || (conclusions[2] != "success" && conclusions[3] != "skipped")
        )
            return Result.Fail<NuGetPublicationSteps>(
                "Publication step outcomes or order are inconsistent."
            );

        return Result.Ok(new NuGetPublicationSteps(conclusions[2]!, conclusions[3]!));
    }

    internal static Result<NuGetFileObservation[]> Validate(
        JsonElement outcome,
        NuGetIntentContext pins,
        NuGetRelease release,
        NuGetHandoffReceipt handoff,
        NuGetPublicationSteps steps,
        DateTimeOffset started,
        DateTimeOffset stored
    )
    {
        var expected = new Dictionary<string, string>
        {
            ["kind"] = pins.OutcomeKind,
            ["intentSha256"] = pins.ContentHash,
            ["intentArtifactId"] = pins.ArtifactId.ToString(
                System.Globalization.CultureInfo.InvariantCulture
            ),
            ["intentArtifactDigest"] = pins.ArchiveDigest["sha256:".Length..],
            ["repository"] = MainPolicy.Repository,
            ["sha"] = release.Sha,
            ["publishRunId"] = pins.RunId.ToString(
                System.Globalization.CultureInfo.InvariantCulture
            ),
            ["publishAttempt"] = "1",
            ["workflowRef"] = pins.Reference,
            ["workflowRevision"] = release.Sha,
            ["loginOutcome"] = steps.Login,
            ["pushOutcome"] = steps.Push,
        };
        if (
            outcome.GetProperty("schemaVersion").GetInt32() != 1
            || outcome.GetProperty("authorizesRetry").GetBoolean()
            || outcome.GetProperty("remoteAcceptanceVerified").GetBoolean()
            || expected.Any(p => outcome.GetProperty(p.Key).GetString() != p.Value)
            || outcome.GetProperty("recordedAt").GetDateTimeOffset() < started
            || outcome.GetProperty("recordedAt").GetDateTimeOffset() > stored
        )
            return Result.Fail<NuGetFileObservation[]>(
                "Outcome identity, timing or authority is invalid."
            );

        var packages = outcome.GetProperty("packages").EnumerateArray().ToArray();
        var intended = handoff.Packages.OrderBy(p => p.File, StringComparer.Ordinal).ToArray();
        if (
            packages.Length != intended.Length
            || !packages
                .Select(p => p.Deserialize<PackageEvidence>(JsonFormat.Options))
                .SequenceEqual(intended)
        )
            return Result.Fail<NuGetFileObservation[]>(
                "Outcomes do not cover the exact ordered publication inventory."
            );

        var observations = new List<NuGetFileObservation>();
        var stopped = false;
        foreach (var (package, index) in packages.Select((p, i) => (p, i)))
        {
            var events = package
                .GetProperty("events")
                .EnumerateArray()
                .Select(e => e.GetString())
                .ToArray();
            var observation = package.GetProperty("cliObservation").GetString();
            var expectedObservation = Classify(events, steps.Push, stopped);
            if (expectedObservation is null || expectedObservation != observation)
                return Result.Fail<NuGetFileObservation[]>(
                    "Outcome events contradict the CLI observation or dispatch order."
                );

            stopped |= expectedObservation != "cli-success";
            observations.Add(new(intended[index], expectedObservation));
        }

        return Result.Ok(observations.ToArray());
    }

    private static string? Classify(string?[] events, string push, bool stopped)
    {
        if (push == "skipped")
            return events.Length == 0 ? "not-dispatched" : null;
        if (push == "success")
            return events is ["started", "cli-success"] ? "cli-success" : null;
        if (events.Length == 0)
            return "unrecorded";
        if (stopped)
            return null;
        if (events is ["started", "cli-success"])
            return "cli-success";
        if (events is ["started"])
            return "uncertain";
        return
            events is ["started", var exit]
            && exit is not null
            && exit.StartsWith("uncertain-exit-", StringComparison.Ordinal)
            && int.TryParse(
                exit["uncertain-exit-".Length..],
                System.Globalization.NumberStyles.None,
                System.Globalization.CultureInfo.InvariantCulture,
                out var code
            )
            && code is > 0 and <= 255
            ? "uncertain"
            : null;
    }
}
