using AndreGoepel.Core;

namespace AndreGoepel.AppFoundation.Sonar;

internal static class NuGetTag
{
    internal static async Task<Result> VerifyAsync(
        IGitHubClient github,
        NuGetRelease release,
        string originalObjectSha,
        CancellationToken ct
    )
    {
        if (release.Tag != "v" + release.Version || !IsSha(originalObjectSha))
            return Result.Fail("An immutable release tag object is required.");

        var reference = await github.GetAsync(
            "git/ref/tags/" + Uri.EscapeDataString(release.Tag),
            ct
        );
        var target = reference.GetProperty("object");
        if (target.GetProperty("sha").GetString() != originalObjectSha)
            return Result.Fail("The release tag object differs from the original push event.");

        for (var depth = 0; depth < 8; depth++)
        {
            var type = target.GetProperty("type").GetString();
            var sha = target.GetProperty("sha").GetString();
            if (type == "commit")
                return sha == release.Sha
                    ? Result.Ok()
                    : Result.Fail("The pinned tag does not resolve to the candidate commit.");
            if (type != "tag" || !IsSha(sha))
                break;

            var annotated = await github.GetAsync("git/tags/" + sha, ct);
            target = annotated.GetProperty("object");
        }

        return Result.Fail("The pinned tag could not be resolved to a commit.");
    }

    internal static bool IsSha(string? value) =>
        value is not null
        && value.Length == 40
        && value.All(c => c is >= '0' and <= '9' or >= 'a' and <= 'f');
}
