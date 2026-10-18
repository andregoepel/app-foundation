using System.Text.RegularExpressions;
using AndreGoepel.Core;

namespace AndreGoepel.AppFoundation.Sonar;

internal sealed partial record NuGetRelease(
    string Root,
    string Sha,
    string Version,
    long ReadinessRunId,
    string? Tag
)
{
    internal const long ReadinessWorkflowId = 387320916;
    internal const string Feed = "https://api.nuget.org/v3/index.json";
    internal static readonly string[] Packages =
    [
        "AndreGoepel.AppFoundation.Core",
        "AndreGoepel.AppFoundation.ServiceDefaults",
        "AndreGoepel.AppFoundation.MailService",
        "AndreGoepel.AppFoundation",
        "AndreGoepel.AppFoundation.Hosting",
        "AndreGoepel.AppFoundation.Aspire",
    ];

    internal static Result<NuGetRelease> Parse(string[] args)
    {
        if (
            args
                is not [
                    "--root",
                    var root,
                    "--nuget-dry-run",
                    "--sha",
                    var sha,
                    "--version",
                    var version,
                    "--readiness-run",
                    var run,
                    .. var tail,
                ]
            || !ShaPattern().IsMatch(sha)
            || !VersionPattern().IsMatch(version)
            || !long.TryParse(run, out var runId)
            || runId <= 0
            || !Directory.Exists(root)
            || (tail.Length != 0 && (tail is not ["--tag", var tag] || tag != "v" + version))
        )
            return Result.Fail<NuGetRelease>(
                "Use --root ROOT --nuget-dry-run --sha SHA --version X.Y.Z --readiness-run ID [--tag vX.Y.Z]."
            );

        return Result.Ok(
            new NuGetRelease(
                Path.GetFullPath(root),
                sha,
                version,
                runId,
                tail.Length == 0 ? null : tail[1]
            )
        );
    }

    internal static Result CheckCredentialBoundary(Func<string, string?> environment)
    {
        string[] names =
        [
            "ACTIONS_ID_TOKEN_REQUEST_TOKEN",
            "ACTIONS_ID_TOKEN_REQUEST_URL",
            "NUGET_API_KEY",
            "NUGET_AUTH_TOKEN",
            "NUGET_SYMBOL_API_KEY",
        ];
        return names.Any(name => !string.IsNullOrEmpty(environment(name)))
            ? Result.Fail("Run preparation without OIDC access or NuGet publication credentials.")
            : Result.Ok();
    }

    [GeneratedRegex("^[a-f0-9]{40}\\z", RegexOptions.CultureInvariant)]
    private static partial Regex ShaPattern();

    [GeneratedRegex(
        "^(0|[1-9][0-9]{0,4})\\.(0|[1-9][0-9]{0,4})\\.(0|[1-9][0-9]{0,4})\\z",
        RegexOptions.CultureInvariant
    )]
    private static partial Regex VersionPattern();
}

internal sealed record PackageEvidence(
    string File,
    string PackageId,
    string Version,
    string Sha256,
    string Feed
);

internal sealed record NuGetDryRunReceipt(
    string Sha,
    string Version,
    long ReadinessRunId,
    int ReadinessAttempt,
    DateTimeOffset ExpiresAt,
    PackageEvidence[] Packages
)
{
    public int SchemaVersion { get; } = 1;
    public bool DryRun { get; } = true;
    public bool ReleaseEligible { get; } = false;
}
