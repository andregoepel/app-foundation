using System.Text;
using System.Text.Json;
using AndreGoepel.Core;

namespace AndreGoepel.AppFoundation.Sonar;

internal static class MainCommand
{
    private static readonly string[] WorkflowPaths =
    [
        ".github/workflows/ci.yml",
        ".github/workflows/e2e.yml",
    ];

    internal static async Task<Result> RunAsync(string root, CancellationToken ct)
    {
        var payloadPath = Environment.GetEnvironmentVariable("GITHUB_EVENT_PATH");
        if (string.IsNullOrEmpty(payloadPath))
            return Result.Fail("Central analysis is only available in the trusted main workflow.");

        using var payload = JsonDocument.Parse(await File.ReadAllTextAsync(payloadPath, ct));
        var parsed = MainEnvironment.Validate(
            root,
            Environment.GetEnvironmentVariable,
            payload.RootElement
        );
        if (parsed.IsFailure)
            return parsed;

        var identity = parsed.Value!;
        var policy = JsonSerializer.Deserialize<MainPolicy>(
            await File.ReadAllTextAsync(Path.Combine(root, "scripts/sonar/main-policy.json"), ct),
            JsonFormat.Options
        )!;
        var configuration = await ValidateConfigurationAsync(root, policy, ct);
        if (configuration.IsFailure)
            return configuration;

        var processes = new ProcessRunner();
        var checkout = await CheckCheckoutAsync(processes, root, identity.Sha, ct);
        if (checkout.IsFailure)
            return checkout;

        var githubToken = TakeCredential("APPFOUNDATION_GITHUB_TOKEN");
        if (string.IsNullOrEmpty(githubToken))
            return Result.Fail("The read-only GitHub credential is missing.");

        using var http = SonarClient.CreateHttpClient();
        using var githubClient = new GitHubClient(githubToken);
        var clock = TimeProvider.System;
        var github = new GitHubEvidence(githubClient, identity, policy, clock, Task.Delay);
        var initial = await github.WaitAsync(ct);
        if (initial.IsFailure)
            return initial;

        var contract = JsonSerializer.Deserialize<AnalysisContract>(
            await File.ReadAllTextAsync(Path.Combine(root, "scripts/sonar/contract.json"), ct),
            JsonFormat.Options
        )!;
        var scanToken = TakeCredential("APPFOUNDATION_SONAR_TOKEN");
        var verifyToken = TakeCredential("APPFOUNDATION_SONAR_VERIFY_TOKEN");
        var options = new AnalysisOptions(
            root,
            new Uri(MainPolicy.SonarUrl),
            MainPolicy.Project,
            MainPolicy.ServerId,
            identity.RunName
        );
        var sonar = new SonarClient(http, options.Url);
        var trust = new MainAnalysisTrust(
            identity,
            github,
            initial.Value!,
            new MainSonarPolicy(sonar, policy, contract.Gate, scanToken, verifyToken)
        );

        var analysis = await new AnalysisRunner(processes, sonar, trust).RunAsync(
            options,
            contract,
            scanToken,
            verifyToken,
            ct
        );
        if (analysis.IsFailure)
            return analysis;

        var finalBoundary = await trust.CheckAsync(ct);
        if (finalBoundary.IsFailure)
            return finalBoundary;

        checkout = await CheckCheckoutAsync(processes, root, identity.Sha, ct);
        if (checkout.IsFailure)
            return checkout;

        var publish = Path.Combine(root, "artifacts/sonar", identity.RunName, "publish");
        var bytes = await File.ReadAllBytesAsync(Path.Combine(publish, "summary.json"), ct);
        var summary = JsonSerializer.Deserialize<AnalysisSummary>(bytes, JsonFormat.Options)!;
        var latest = await sonar.GetAsync(
            "project_analyses/search",
            verifyToken,
            ct,
            ("project", MainPolicy.Project),
            ("ps", "1")
        );
        var analyses = latest.GetProperty("analyses");
        if (analyses.GetArrayLength() != 1)
            return Result.Fail("The submitted Sonar analysis is no longer available.");

        var finalAnalysis = await trust.VerifyAsync(summary, analyses[0], ct);
        if (finalAnalysis.IsFailure)
            return finalAnalysis;

        if (
            summary.State != "complete"
            || summary.SourceSha256 != await SourceFiles.SnapshotAsync(root, ct)
        )
            return Result.Fail("Analysis cleanup or the final source snapshot did not verify.");

        var now = clock.GetUtcNow();
        var expires = initial.Value!.Min(w => w.StartedAt.AddMinutes(policy.MaximumRunAgeMinutes));
        if (now.AddMinutes(policy.EvidenceLifetimeMinutes) < expires)
            expires = now.AddMinutes(policy.EvidenceLifetimeMinutes);
        if (expires <= now)
            return Result.Fail("Mandatory evidence expired before readiness could be recorded.");

        var receipt = new ReadinessReceipt(
            1,
            MainPolicy.Repository,
            identity,
            MainPolicy.WorkflowPath,
            identity.Sha,
            policy.Version,
            policy.Fingerprint(),
            summary.ContractSha256!,
            SourceFiles.Hash(bytes),
            summary.SourceSha256!,
            policy.BaselineAnalysisId!,
            summary.TaskId!,
            summary.AnalysisId!,
            initial.Value!,
            now,
            expires
        );
        await File.WriteAllTextAsync(
            Path.Combine(publish, "readiness.json"),
            JsonSerializer.Serialize(receipt, JsonFormat.Options),
            ct
        );

        return Result.Ok();
    }

    private static string TakeCredential(string name)
    {
        var value = Environment.GetEnvironmentVariable(name) ?? "";
        Environment.SetEnvironmentVariable(name, null);

        return value;
    }

    internal static async Task<Result> CheckCheckoutAsync(
        IProcessRunner processes,
        string root,
        string sha,
        CancellationToken ct
    )
    {
        var head = await processes.RunAsync(root, ["git", "rev-parse", "HEAD"], ct);
        if (head.IsFailure || head.Value!.ExitCode != 0 || head.Value.Text.Trim() != sha)
            return Result.Fail("The checkout is not the expected main revision.");

        var status = await processes.RunAsync(
            root,
            ["git", "status", "--porcelain", "--untracked-files=normal"],
            ct
        );

        return
            status.IsSuccess
            && status.Value!.ExitCode == 0
            && string.IsNullOrWhiteSpace(status.Value.Text)
            ? Result.Ok()
            : Result.Fail(
                "Trusted analysis requires a clean checkout, including untracked sources."
            );
    }

    internal static async Task<Result> ValidateConfigurationAsync(
        string root,
        MainPolicy policy,
        CancellationToken ct
    )
    {
        if (
            string.IsNullOrWhiteSpace(policy.Version)
            || policy.MaximumRunAgeMinutes is < 1 or > 1440
            || policy.EvidenceLifetimeMinutes is < 1 or > 60
            || policy.Workflows.Length != 2
            || !policy
                .Workflows.Select(w => w.Path)
                .Order(StringComparer.Ordinal)
                .SequenceEqual(WorkflowPaths)
            || policy.Conditions.Length != 6
            || policy.Profiles.Length == 0
            || (
                policy.BaselineAnalysisId is not null
                && !Guid.TryParse(policy.BaselineAnalysisId, out _)
            )
        )
            return Result.Fail("Incomplete or invalid reviewed main policy.");

        foreach (var workflow in policy.Workflows)
        {
            var content = await File.ReadAllTextAsync(Path.Combine(root, workflow.Path), ct);
            if (
                SourceFiles.Hash(
                    Encoding.UTF8.GetBytes(content.Replace("\r\n", "\n", StringComparison.Ordinal))
                ) != workflow.FileSha256
            )
                return Result.Fail(
                    "An existing workflow changed without reviewing its readiness policy."
                );
        }

        return Result.Ok();
    }
}
