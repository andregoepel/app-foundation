using System.Globalization;
using System.Text.Json;
using AndreGoepel.Core;

namespace AndreGoepel.AppFoundation.Sonar;

internal sealed record NuGetPreparationContext(
    NuGetRelease Release,
    string TagObjectSha,
    long RunId,
    int Attempt
)
{
    internal static async Task<Result<NuGetPreparationContext>> ReadAsync(
        NuGetRelease release,
        Func<string, string?> environment,
        CancellationToken ct
    )
    {
        var path = environment("GITHUB_EVENT_PATH");
        if (string.IsNullOrEmpty(path) || !Path.IsPathFullyQualified(path))
            return Result.Fail<NuGetPreparationContext>("The original tag-push event is required.");

        var file = new FileInfo(path);
        if (
            !file.Exists
            || file.Length > 1048576
            || (file.Attributes & FileAttributes.ReparsePoint) != 0
        )
            return Result.Fail<NuGetPreparationContext>(
                "The original tag-push event is missing or invalid."
            );

        var bytes = await File.ReadAllBytesAsync(path, ct);
        if (bytes.Length > 1048576)
            return Result.Fail<NuGetPreparationContext>(
                "The original tag-push event is too large."
            );

        using var document = JsonDocument.Parse(bytes);
        return Capture(release, environment, document.RootElement);
    }

    internal static Result<NuGetPreparationContext> Capture(
        NuGetRelease release,
        Func<string, string?> environment,
        JsonElement payload
    )
    {
        var reference = "refs/tags/" + release.Tag;
        if (
            !NuGetRelease.IsPreparationTag(release.Tag, release.Version)
            || environment("GITHUB_ACTIONS") != "true"
            || environment("GITHUB_REPOSITORY") != MainPolicy.Repository
            || environment("GITHUB_EVENT_NAME") != "push"
            || environment("GITHUB_REF") != reference
            || environment("GITHUB_REF_TYPE") != "tag"
            || environment("GITHUB_SHA") != release.Sha
            || environment("GITHUB_WORKFLOW_SHA") != release.Sha
            || environment("GITHUB_WORKFLOW_REF")
                != $"{MainPolicy.Repository}/{NuGetPreparationPolicy.WorkflowPath}@{reference}"
            || !Path.IsPathFullyQualified(release.Root)
            || environment("GITHUB_WORKSPACE") != Path.GetFullPath(release.Root)
            || !long.TryParse(
                environment("GITHUB_RUN_ID"),
                CultureInfo.InvariantCulture,
                out var runId
            )
            || runId <= 0
            || !int.TryParse(
                environment("GITHUB_RUN_ATTEMPT"),
                CultureInfo.InvariantCulture,
                out var attempt
            )
            || attempt <= 0
        )
            return Result.Fail<NuGetPreparationContext>(
                "Preparation requires the reviewed tag-push workflow context."
            );

        var after = payload.GetProperty("after").GetString();
        if (
            payload.GetProperty("repository").GetProperty("full_name").GetString()
                != MainPolicy.Repository
            || payload.GetProperty("ref").GetString() != reference
            || !payload.GetProperty("created").GetBoolean()
            || payload.GetProperty("deleted").GetBoolean()
            || payload.GetProperty("forced").GetBoolean()
            || payload.GetProperty("before").GetString() != new string('0', 40)
            || !NuGetTag.IsSha(after)
            || after == new string('0', 40)
        )
            return Result.Fail<NuGetPreparationContext>(
                "Only an original version-tag creation event can prepare packages."
            );

        return Result.Ok(new NuGetPreparationContext(release, after!, runId, attempt));
    }
}
