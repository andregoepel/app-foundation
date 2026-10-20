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

        var opened = await ReleaseVerificationSession.OpenAsync(release.Root, ct);
        if (opened.IsFailure)
            return opened;
        using var session = opened.Value!;
        var processes = new ProcessRunner();
        var github = session.GitHub;
        var readiness = session.Readiness;
        if (input.Handoff is not null)
        {
            var consumer = new NuGetConsumer(
                readiness,
                github,
                session.Artifacts,
                input.Policy!,
                TimeProvider.System
            );
            return await RunHandoffAsync(consumer, input.Handoff, Console.Out, ct);
        }

        var dryRun = new NuGetDryRun(
            readiness,
            new NuGetPackageBuilder(processes, session.Contract)
        );
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
        CancellationToken ct,
        Func<string, string?>? environment = null
    )
    {
        if (options.VerifyOnly)
        {
            var verified = await consumer.VerifyAsync(options, ct);
            if (verified.IsSuccess && options.Inspection is { } inspection)
            {
                var inspected = await consumer.InspectAsync(
                    options,
                    inspection,
                    verified.Value!,
                    ct
                );
                if (inspected.IsSuccess)
                    await output.WriteLineAsync(
                        JsonSerializer.Serialize(inspected.Value, JsonFormat.Options).AsMemory(),
                        ct
                    );
                return inspected;
            }

            if (verified.IsSuccess && options.VerifyStoredIntent)
            {
                var pins = NuGetIntentContext.Read(
                    options,
                    environment ?? Environment.GetEnvironmentVariable
                );
                verified = pins.IsSuccess
                    ? await consumer.VerifyIntentAsync(options, pins.Value!, verified.Value!, ct)
                    : Result.Fail<NuGetHandoffReceipt>(pins.Error);
            }
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
            || args.Contains("--nuget-verify-intent", StringComparer.Ordinal)
            || args.Contains("--nuget-inspect-publication", StringComparer.Ordinal)
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
