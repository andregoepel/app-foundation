using System.Text.Json;
using AndreGoepel.Core;

namespace AndreGoepel.AppFoundation.Sonar;

internal static class NuGetCommand
{
    internal static async Task<Result> RunAsync(string[] args, CancellationToken ct)
    {
        var parsed = NuGetRelease.Parse(args);
        if (parsed.IsFailure)
            return parsed;
        var boundary = NuGetRelease.CheckCredentialBoundary(Environment.GetEnvironmentVariable);
        if (boundary.IsFailure)
            return boundary;

        var release = parsed.Value!;
        NuGetPreparationContext? preparation = null;
        if (args.Contains("--nuget-prepare", StringComparer.Ordinal))
        {
            var captured = await NuGetPreparationContext.ReadAsync(
                release,
                Environment.GetEnvironmentVariable,
                ct
            );
            if (captured.IsFailure)
                return captured;
            preparation = captured.Value!;
        }

        var policy = JsonSerializer.Deserialize<MainPolicy>(
            await File.ReadAllTextAsync(
                Path.Combine(release.Root, "scripts/sonar/main-policy.json"),
                ct
            ),
            JsonFormat.Options
        )!;
        var configuration = await MainCommand.ValidateConfigurationAsync(release.Root, policy, ct);
        if (configuration.IsFailure)
            return configuration;
        var contract = JsonSerializer.Deserialize<AnalysisContract>(
            await File.ReadAllTextAsync(
                Path.Combine(release.Root, "scripts/sonar/contract.json"),
                ct
            ),
            JsonFormat.Options
        )!;

        var githubToken = TakeCredential("APPFOUNDATION_GITHUB_TOKEN");
        var scanToken = TakeCredential("APPFOUNDATION_SONAR_TOKEN");
        var verifyToken = TakeCredential("APPFOUNDATION_SONAR_VERIFY_TOKEN");
        if (githubToken.Length == 0 || scanToken.Length == 0 || verifyToken.Length == 0)
            return Result.Fail(
                "Read-only GitHub and both existing Sonar verification credentials are required."
            );

        using var client = SonarClient.CreateHttpClient();
        var processes = new ProcessRunner();
        var github = new GitHubClient(client, githubToken);
        var readiness = new ReleaseReadiness(
            github,
            new GitHubArtifacts(client, githubToken),
            new SonarClient(client, new Uri(MainPolicy.SonarUrl)),
            processes,
            TimeProvider.System,
            policy,
            contract,
            scanToken,
            verifyToken
        );
        var dryRun = new NuGetDryRun(readiness, new NuGetPackageBuilder(processes, contract));
        if (preparation is not null)
        {
            var origin = await new NuGetPreparation(
                dryRun,
                new NuGetHandoff(readiness, github, TimeProvider.System),
                github,
                TimeProvider.System
            ).RunAsync(preparation, ct);
            if (origin.IsSuccess)
                Console.WriteLine(JsonSerializer.Serialize(origin.Value, JsonFormat.Options));
            return origin;
        }

        var result = await dryRun.RunAsync(release, ct);
        if (result.IsSuccess)
            Console.WriteLine(
                $"NuGet dry run complete: {result.Value}. No publication was performed or authorized."
            );

        return result;
    }

    private static string TakeCredential(string name)
    {
        var value = Environment.GetEnvironmentVariable(name) ?? "";
        Environment.SetEnvironmentVariable(name, null);
        return value;
    }
}
