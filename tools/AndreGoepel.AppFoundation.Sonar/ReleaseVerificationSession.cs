using System.Text.Json;
using AndreGoepel.Core;

namespace AndreGoepel.AppFoundation.Sonar;

internal sealed class ReleaseVerificationSession(
    HttpClient client,
    IGitHubClient github,
    IGitHubArtifacts artifacts,
    IReleaseReadiness readiness,
    AnalysisContract contract
) : IDisposable
{
    internal IGitHubClient GitHub { get; } = github;
    internal IGitHubArtifacts Artifacts { get; } = artifacts;
    internal IReleaseReadiness Readiness { get; } = readiness;
    internal AnalysisContract Contract { get; } = contract;

    internal static async Task<Result<ReleaseVerificationSession>> OpenAsync(
        string root,
        CancellationToken ct
    )
    {
        var policy = JsonSerializer.Deserialize<MainPolicy>(
            await File.ReadAllTextAsync(Path.Combine(root, "scripts/sonar/main-policy.json"), ct),
            JsonFormat.Options
        )!;
        var configuration = await MainCommand.ValidateConfigurationAsync(root, policy, ct);
        if (configuration.IsFailure)
            return Result.Fail<ReleaseVerificationSession>(configuration.Error);
        var contract = JsonSerializer.Deserialize<AnalysisContract>(
            await File.ReadAllTextAsync(Path.Combine(root, "scripts/sonar/contract.json"), ct),
            JsonFormat.Options
        )!;

        var githubToken = TakeCredential("APPFOUNDATION_GITHUB_TOKEN");
        var scanToken = TakeCredential("APPFOUNDATION_SONAR_TOKEN");
        var verifyToken = TakeCredential("APPFOUNDATION_SONAR_VERIFY_TOKEN");
        if (githubToken.Length == 0 || scanToken.Length == 0 || verifyToken.Length == 0)
            return Result.Fail<ReleaseVerificationSession>(
                "Read-only GitHub and both existing Sonar verification credentials are required."
            );

        var client = SonarClient.CreateHttpClient();
        var github = new GitHubClient(githubToken);
        var artifacts = new GitHubArtifacts(client, githubToken);
        var readiness = new ReleaseReadiness(
            github,
            artifacts,
            new SonarClient(client, new Uri(MainPolicy.SonarUrl)),
            new ProcessRunner(),
            TimeProvider.System,
            policy,
            contract,
            scanToken,
            verifyToken
        );
        return Result.Ok(
            new ReleaseVerificationSession(client, github, artifacts, readiness, contract)
        );
    }

    public void Dispose()
    {
        (GitHub as IDisposable)?.Dispose();
        client.Dispose();
    }

    private static string TakeCredential(string name)
    {
        var value = Environment.GetEnvironmentVariable(name) ?? "";
        Environment.SetEnvironmentVariable(name, null);
        return value;
    }
}
