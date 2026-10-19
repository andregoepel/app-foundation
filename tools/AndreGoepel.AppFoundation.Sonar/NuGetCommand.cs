using System.Text.Json;
using AndreGoepel.Core;

namespace AndreGoepel.AppFoundation.Sonar;

internal static class NuGetCommand
{
    internal static async Task<Result> RunAsync(string[] args, CancellationToken ct)
    {
        var boundary = NuGetRelease.CheckCredentialBoundary(Environment.GetEnvironmentVariable);
        if (boundary.IsFailure)
            return boundary;

        var parsed = await NuGetCommandInput.ReadAsync(args, ct);
        if (parsed.IsFailure)
            return parsed;
        var input = parsed.Value!;
        var release = input.Release;

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
        if (input.Handoff is not null)
        {
            var consumer = new NuGetConsumer(
                readiness,
                github,
                new GitHubArtifacts(client, githubToken),
                input.Policy!,
                TimeProvider.System
            );
            return await RunHandoffAsync(consumer, input.Handoff, Console.Out, ct);
        }

        var dryRun = new NuGetDryRun(readiness, new NuGetPackageBuilder(processes, contract));
        if (input.Preparation is not null)
        {
            var origin = await new NuGetPreparation(
                dryRun,
                new NuGetHandoff(readiness, github, TimeProvider.System),
                github,
                TimeProvider.System
            ).RunAsync(input.Preparation, ct);
            return await NuGetWorkflowOutput.CompleteAsync(
                origin,
                input.Preparation,
                Environment.GetEnvironmentVariable,
                ct
            );
        }

        var result = await dryRun.RunAsync(release, ct);
        if (result.IsSuccess)
            Console.WriteLine(
                $"NuGet dry run complete: {result.Value}. No publication was performed or authorized."
            );

        return result;
    }

    internal static async Task<Result> RunHandoffAsync(
        NuGetConsumer consumer,
        NuGetHandoffOptions options,
        TextWriter output,
        CancellationToken ct
    )
    {
        if (options.VerifyOnly)
        {
            var verified = await consumer.VerifyAsync(options, ct);
            if (verified.IsSuccess)
                await output.WriteLineAsync(
                    JsonSerializer.Serialize(verified.Value, JsonFormat.Options).AsMemory(),
                    ct
                );
            return verified;
        }

        var consumed = await consumer.RunAsync(options, ct);
        if (consumed.IsSuccess)
            await output.WriteLineAsync(
                JsonSerializer.Serialize(consumed.Value, JsonFormat.Options).AsMemory(),
                ct
            );
        return consumed;
    }

    private static string TakeCredential(string name)
    {
        var value = Environment.GetEnvironmentVariable(name) ?? "";
        Environment.SetEnvironmentVariable(name, null);
        return value;
    }
}

internal sealed record NuGetCommandInput(
    NuGetRelease Release,
    NuGetHandoffOptions? Handoff,
    NuGetPreparationPolicy? Policy,
    NuGetPreparationContext? Preparation
)
{
    internal static async Task<Result<NuGetCommandInput>> ReadAsync(
        string[] args,
        CancellationToken ct
    )
    {
        if (
            args.Contains("--nuget-rehearse", StringComparer.Ordinal)
            || args.Contains("--nuget-verify-handoff", StringComparer.Ordinal)
        )
        {
            var options = NuGetHandoffOptions.Parse(args);
            if (options.IsFailure)
                return Result.Fail<NuGetCommandInput>(options.Error);
            var handoff = options.Value!;
            var registered = await NuGetPreparationPolicy.ReadAsync(handoff.Input.Release.Root, ct);
            return registered.IsSuccess
                ? Result.Ok(
                    new NuGetCommandInput(handoff.Input.Release, handoff, registered.Value!, null)
                )
                : Result.Fail<NuGetCommandInput>(registered.Error);
        }

        var parsed = NuGetRelease.Parse(args);
        if (parsed.IsFailure)
            return Result.Fail<NuGetCommandInput>(parsed.Error);
        var release = parsed.Value!;
        if (!args.Contains("--nuget-prepare", StringComparer.Ordinal))
            return Result.Ok(new NuGetCommandInput(release, null, null, null));

        var captured = await NuGetPreparationContext.ReadAsync(
            release,
            Environment.GetEnvironmentVariable,
            ct
        );
        return captured.IsSuccess
            ? Result.Ok(new NuGetCommandInput(release, null, null, captured.Value!))
            : Result.Fail<NuGetCommandInput>(captured.Error);
    }
}
