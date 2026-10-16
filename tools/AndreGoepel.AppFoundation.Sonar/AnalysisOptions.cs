using System.Text.RegularExpressions;
using AndreGoepel.Core;

namespace AndreGoepel.AppFoundation.Sonar;

internal sealed partial record AnalysisOptions(
    string Root,
    Uri Url,
    string Project,
    string ServerId,
    string RunName
)
{
    internal static Result<AnalysisOptions> Parse(string[] args)
    {
        var values = new Dictionary<string, string>(StringComparer.Ordinal);
        string[] known = ["--root", "--url", "--project", "--server-id", "--run-name"];

        for (var i = 0; i < args.Length; i += 2)
        {
            if (
                i + 1 == args.Length
                || !known.Contains(args[i])
                || !values.TryAdd(args[i], args[i + 1])
            )
                return Result.Fail<AnalysisOptions>(
                    "Use --root, --server-id and --run-name, with optional --url and --project; each option accepts one value."
                );
        }

        if (
            !values.TryGetValue("--root", out var root)
            || !Path.IsPathFullyQualified(root)
            || !values.TryGetValue("--server-id", out var id)
            || string.IsNullOrWhiteSpace(id)
            || !values.TryGetValue("--run-name", out var run)
            || !NamePattern().IsMatch(run)
            || !Uri.TryCreate(
                values.GetValueOrDefault("--url", "http://127.0.0.1:9000"),
                UriKind.Absolute,
                out var url
            )
        )
            return Result.Fail<AnalysisOptions>(
                "An absolute --root, separate --server-id and unique lowercase --run-name are required."
            );

        var options = new AnalysisOptions(
            Path.GetFullPath(root),
            url,
            values.GetValueOrDefault("--project", "app-foundation-local"),
            id,
            run
        );
        var validation = options.ValidateTarget();

        return validation.IsSuccess
            ? Result.Ok(options)
            : Result.Fail<AnalysisOptions>(validation.Error);
    }

    internal Result ValidateTarget()
    {
        if (
            Url.Scheme != "http"
            || !new[] { "localhost", "127.0.0.1", "[::1]" }.Contains(Url.Host)
            || Url.Port != 9000
            || Url.AbsolutePath != "/"
            || Url.UserInfo.Length != 0
            || Url.Query.Length != 0
            || Url.Fragment.Length != 0
            || !ProjectPattern().IsMatch(Project)
            || string.IsNullOrWhiteSpace(ServerId)
            || ServerId == "AA2DD71C-AaFDycIuQLOXJXX6YWNZ"
        )
            return Result.Fail(
                "Only the separate loopback Sonar prototype on port 9000 and app-foundation-local projects are allowed."
            );

        return Result.Ok();
    }

    [GeneratedRegex("^[a-z0-9]+(?:-[a-z0-9]+)*$", RegexOptions.CultureInvariant)]
    private static partial Regex NamePattern();

    [GeneratedRegex("^app-foundation-local(?:-[a-z0-9]+)*$", RegexOptions.CultureInvariant)]
    private static partial Regex ProjectPattern();
}
