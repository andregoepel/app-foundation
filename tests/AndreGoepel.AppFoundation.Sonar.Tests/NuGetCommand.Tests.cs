using System.Diagnostics;
using AndreGoepel.AppFoundation.Sonar;
using Xunit;

namespace AndreGoepel.AppFoundation.Sonar.Tests;

public sealed class NuGetCommandTests
{
    private const string Sentinel = "synthetic-publication-secret-244";
    private static readonly string[] ConfigurationFiles =
    [
        "scripts/sonar/main-policy.json",
        "scripts/sonar/contract.json",
        ".github/workflows/ci.yml",
        ".github/workflows/e2e.yml",
    ];

    [Theory]
    [InlineData("ACTIONS_ID_TOKEN_REQUEST_TOKEN", "--nuget-dry-run")]
    [InlineData("ACTIONS_ID_TOKEN_REQUEST_URL", "--nuget-dry-run")]
    [InlineData("NUGET_API_KEY", "--nuget-dry-run")]
    [InlineData("NUGET_AUTH_TOKEN", "--nuget-dry-run")]
    [InlineData("NUGET_SYMBOL_API_KEY", "--nuget-dry-run")]
    [InlineData("ACTIONS_ID_TOKEN_REQUEST_TOKEN", "--nuget-prepare")]
    [InlineData("ACTIONS_ID_TOKEN_REQUEST_URL", "--nuget-prepare")]
    [InlineData("NUGET_API_KEY", "--nuget-prepare")]
    [InlineData("NUGET_AUTH_TOKEN", "--nuget-prepare")]
    [InlineData("NUGET_SYMBOL_API_KEY", "--nuget-prepare")]
    [InlineData("ACTIONS_ID_TOKEN_REQUEST_TOKEN", "--nuget-rehearse")]
    [InlineData("ACTIONS_ID_TOKEN_REQUEST_URL", "--nuget-rehearse")]
    [InlineData("NUGET_API_KEY", "--nuget-rehearse")]
    [InlineData("NUGET_AUTH_TOKEN", "--nuget-rehearse")]
    [InlineData("NUGET_SYMBOL_API_KEY", "--nuget-rehearse")]
    [InlineData("ACTIONS_ID_TOKEN_REQUEST_TOKEN", "--nuget-verify-handoff")]
    [InlineData("ACTIONS_ID_TOKEN_REQUEST_URL", "--nuget-verify-handoff")]
    [InlineData("NUGET_API_KEY", "--nuget-verify-handoff")]
    [InlineData("NUGET_AUTH_TOKEN", "--nuget-verify-handoff")]
    [InlineData("NUGET_SYMBOL_API_KEY", "--nuget-verify-handoff")]
    [InlineData("ACTIONS_ID_TOKEN_REQUEST_TOKEN", "--nuget-verify-intent")]
    [InlineData("ACTIONS_ID_TOKEN_REQUEST_URL", "--nuget-verify-intent")]
    [InlineData("NUGET_API_KEY", "--nuget-verify-intent")]
    [InlineData("NUGET_AUTH_TOKEN", "--nuget-verify-intent")]
    [InlineData("NUGET_SYMBOL_API_KEY", "--nuget-verify-intent")]
    [InlineData("ACTIONS_ID_TOKEN_REQUEST_TOKEN", "--nuget-inspect-publication")]
    [InlineData("ACTIONS_ID_TOKEN_REQUEST_URL", "--nuget-inspect-publication")]
    [InlineData("NUGET_API_KEY", "--nuget-inspect-publication")]
    [InlineData("NUGET_AUTH_TOKEN", "--nuget-inspect-publication")]
    [InlineData("NUGET_SYMBOL_API_KEY", "--nuget-inspect-publication")]
    [InlineData("ACTIONS_ID_TOKEN_REQUEST_TOKEN", "--staging-contract")]
    [InlineData("ACTIONS_ID_TOKEN_REQUEST_URL", "--staging-contract")]
    [InlineData("NUGET_API_KEY", "--staging-contract")]
    [InlineData("NUGET_AUTH_TOKEN", "--staging-contract")]
    [InlineData("NUGET_SYMBOL_API_KEY", "--staging-contract")]
    public async Task Run_PublicationCredential_ExitsBeforeReadingConfiguration(
        string name,
        string mode
    )
    {
        // Arrange
        using var fixture = new RunnerFixture();
        var start = Start(fixture.Root, mode);
        start.Environment[name] = Sentinel;

        // Act
        var output = await ExecuteAsync(start, TestContext.Current.CancellationToken);

        // Assert
        Assert.Equal(1, output.ExitCode);
        Assert.Contains(
            "Run preparation without OIDC access or NuGet publication credentials.",
            output.Text
        );
        Assert.DoesNotContain(Sentinel, output.Text);
        Assert.False(Directory.Exists(Path.Combine(fixture.Root, "artifacts")));
    }

    [Theory]
    [InlineData("ACTIONS_ID_TOKEN_REQUEST_TOKEN", "--nuget-dry-run")]
    [InlineData("ACTIONS_ID_TOKEN_REQUEST_URL", "--nuget-dry-run")]
    [InlineData("NUGET_API_KEY", "--nuget-dry-run")]
    [InlineData("NUGET_AUTH_TOKEN", "--nuget-dry-run")]
    [InlineData("NUGET_SYMBOL_API_KEY", "--nuget-dry-run")]
    [InlineData("ACTIONS_ID_TOKEN_REQUEST_TOKEN", "--nuget-prepare")]
    [InlineData("ACTIONS_ID_TOKEN_REQUEST_URL", "--nuget-prepare")]
    [InlineData("NUGET_API_KEY", "--nuget-prepare")]
    [InlineData("NUGET_AUTH_TOKEN", "--nuget-prepare")]
    [InlineData("NUGET_SYMBOL_API_KEY", "--nuget-prepare")]
    [InlineData("ACTIONS_ID_TOKEN_REQUEST_TOKEN", "--nuget-rehearse")]
    [InlineData("ACTIONS_ID_TOKEN_REQUEST_URL", "--nuget-rehearse")]
    [InlineData("NUGET_API_KEY", "--nuget-rehearse")]
    [InlineData("NUGET_AUTH_TOKEN", "--nuget-rehearse")]
    [InlineData("NUGET_SYMBOL_API_KEY", "--nuget-rehearse")]
    [InlineData("ACTIONS_ID_TOKEN_REQUEST_TOKEN", "--nuget-verify-handoff")]
    [InlineData("ACTIONS_ID_TOKEN_REQUEST_URL", "--nuget-verify-handoff")]
    [InlineData("NUGET_API_KEY", "--nuget-verify-handoff")]
    [InlineData("NUGET_AUTH_TOKEN", "--nuget-verify-handoff")]
    [InlineData("NUGET_SYMBOL_API_KEY", "--nuget-verify-handoff")]
    [InlineData("ACTIONS_ID_TOKEN_REQUEST_TOKEN", "--nuget-verify-intent")]
    [InlineData("ACTIONS_ID_TOKEN_REQUEST_URL", "--nuget-verify-intent")]
    [InlineData("NUGET_API_KEY", "--nuget-verify-intent")]
    [InlineData("NUGET_AUTH_TOKEN", "--nuget-verify-intent")]
    [InlineData("NUGET_SYMBOL_API_KEY", "--nuget-verify-intent")]
    [InlineData("ACTIONS_ID_TOKEN_REQUEST_TOKEN", "--nuget-inspect-publication")]
    [InlineData("ACTIONS_ID_TOKEN_REQUEST_URL", "--nuget-inspect-publication")]
    [InlineData("NUGET_API_KEY", "--nuget-inspect-publication")]
    [InlineData("NUGET_AUTH_TOKEN", "--nuget-inspect-publication")]
    [InlineData("NUGET_SYMBOL_API_KEY", "--nuget-inspect-publication")]
    [InlineData("ACTIONS_ID_TOKEN_REQUEST_TOKEN", "--staging-contract")]
    [InlineData("ACTIONS_ID_TOKEN_REQUEST_URL", "--staging-contract")]
    [InlineData("NUGET_API_KEY", "--staging-contract")]
    [InlineData("NUGET_AUTH_TOKEN", "--staging-contract")]
    [InlineData("NUGET_SYMBOL_API_KEY", "--staging-contract")]
    public async Task Bootstrap_PublicationCredential_ExitsBeforeRestoreOrBuild(
        string name,
        string mode
    )
    {
        // Arrange
        using var fixture = new RunnerFixture();
        var script = OperatingSystem.IsWindows() ? "analyze.ps1" : "analyze.sh";
        var copy = Path.Combine(fixture.Root, "scripts/sonar", script);
        File.Copy(Path.Combine(RepositoryRoot(), "scripts/sonar", script), copy);
        string[] command = OperatingSystem.IsWindows()
            ? ["pwsh", "-NoProfile", "-File", copy, mode]
            : ["bash", copy, mode];
        var start = ProcessRunner.CreateStartInfo(fixture.Root, command);
        start.Environment[name] = Sentinel;

        // Act
        var output = await ExecuteAsync(start, TestContext.Current.CancellationToken);

        // Assert
        Assert.NotEqual(0, output.ExitCode);
        Assert.Contains(
            "Run preparation without OIDC access or NuGet publication credentials.",
            output.Text
        );
        Assert.DoesNotContain(Sentinel, output.Text);
        Assert.False(Directory.Exists(Path.Combine(fixture.Root, "artifacts")));
    }

    [Theory]
    [InlineData("--publish", "true")]
    [InlineData("--source", "https://other-feed.example")]
    [InlineData("--symbol-source", "https://symbols.example")]
    [InlineData("--api-key", Sentinel)]
    [InlineData("--allow-stale", "true")]
    [InlineData("--tag", "v1.0.0")]
    public async Task Run_ManualPublicationOrBypassOption_ExitsBeforePreparation(
        string flag,
        string value
    )
    {
        // Arrange
        using var fixture = new RunnerFixture();
        var start = Start(fixture.Root);
        start.ArgumentList.Add(flag);
        start.ArgumentList.Add(value);

        // Act
        var output = await ExecuteAsync(start, TestContext.Current.CancellationToken);

        // Assert
        Assert.Equal(1, output.ExitCode);
        Assert.Contains("Use --root ROOT --nuget-dry-run", output.Text);
        Assert.DoesNotContain(value, output.Text);
        Assert.False(Directory.Exists(Path.Combine(fixture.Root, "artifacts")));
    }

    [Theory]
    [InlineData("--nuget-dry-run")]
    [InlineData("--nuget-rehearse")]
    [InlineData("--nuget-verify-handoff")]
    [InlineData("--nuget-verify-intent")]
    public async Task Run_MissingReadCredentials_ExitsWithoutPreparingPackages(string mode)
    {
        // Arrange
        using var fixture = new RunnerFixture();
        var repository = RepositoryRoot();
        foreach (var path in ConfigurationFiles)
        {
            var target = Path.Combine(fixture.Root, path);
            Directory.CreateDirectory(Path.GetDirectoryName(target)!);
            File.Copy(Path.Combine(repository, path), target, overwrite: true);
        }
        fixture.Write(
            NuGetPreparationPolicy.ConfigurationPath,
            System.Text.Json.JsonSerializer.Serialize(
                NuGetPreparationPolicyTests.Approved(),
                JsonFormat.Options
            )
        );
        var start = Start(fixture.Root, mode);

        // Act
        var output = await ExecuteAsync(start, TestContext.Current.CancellationToken);

        // Assert
        Assert.Equal(1, output.ExitCode);
        Assert.Contains(
            "Read-only GitHub and both existing Sonar verification credentials are required.",
            output.Text
        );
        Assert.False(Directory.Exists(Path.Combine(fixture.Root, "artifacts")));
    }

    [Theory]
    [InlineData("missing-event")]
    [InlineData("malformed-event")]
    [InlineData("manual-context")]
    [InlineData("retarget-event")]
    public async Task Run_InvalidPreparationContext_ExitsBeforeConfigurationOrNetwork(string fault)
    {
        // Arrange
        using var fixture = new RunnerFixture();
        var start = Start(fixture.Root, "--nuget-prepare");
        var release = NuGetPreparationContextTests.Release() with { Root = fixture.Root };
        foreach (var pair in NuGetPreparationContextTests.EnvironmentFor(release))
            start.Environment[pair.Key] = pair.Value;
        var path = Path.Combine(fixture.Root, "event.json");
        start.Environment["GITHUB_EVENT_PATH"] = path;
        var payload = NuGetPreparationContextTests.Payload(false);
        if (fault == "manual-context")
            start.Environment["GITHUB_EVENT_NAME"] = "workflow_dispatch";
        if (fault == "retarget-event")
            payload["created"] = false;
        if (fault != "missing-event")
            await File.WriteAllTextAsync(
                path,
                fault == "malformed-event" ? "{" : payload.ToJsonString(),
                TestContext.Current.CancellationToken
            );

        // Act
        var output = await ExecuteAsync(start, TestContext.Current.CancellationToken);

        // Assert
        Assert.Equal(1, output.ExitCode);
        Assert.False(Directory.Exists(Path.Combine(fixture.Root, "artifacts")));
        Assert.DoesNotContain("IOException", output.Text);
    }

    [Theory]
    [InlineData("missing", "--nuget-rehearse")]
    [InlineData("unregistered", "--nuget-rehearse")]
    [InlineData("malformed", "--nuget-rehearse")]
    [InlineData("missing", "--nuget-verify-handoff")]
    [InlineData("unregistered", "--nuget-verify-handoff")]
    [InlineData("malformed", "--nuget-verify-handoff")]
    public async Task Run_UnregisteredConsumer_ExitsBeforeMainConfigurationOrNetwork(
        string state,
        string mode
    )
    {
        // Arrange
        using var fixture = new RunnerFixture();
        if (state != "missing")
            fixture.Write(
                NuGetPreparationPolicy.ConfigurationPath,
                state == "malformed"
                    ? "{"
                    : """
                    {"workflowId":0,"workflowSha256":"","maximumAgeMinutes":120,"jobs":{}}
                    """
            );
        var start = Start(fixture.Root, mode);
        start.Environment["APPFOUNDATION_GITHUB_TOKEN"] = Sentinel;
        start.Environment["APPFOUNDATION_SONAR_TOKEN"] = Sentinel;
        start.Environment["APPFOUNDATION_SONAR_VERIFY_TOKEN"] = Sentinel;

        // Act
        var output = await ExecuteAsync(start, TestContext.Current.CancellationToken);

        // Assert
        Assert.Equal(1, output.ExitCode);
        Assert.Contains(
            state == "malformed" ? "JsonException" : "A reviewed preparation policy is required.",
            output.Text
        );
        Assert.DoesNotContain(Sentinel, output.Text);
        Assert.False(Directory.Exists(Path.Combine(fixture.Root, "artifacts")));
    }

    [Theory]
    [InlineData("--publish", "true", "--nuget-rehearse")]
    [InlineData("--policy", "untrusted.json", "--nuget-rehearse")]
    [InlineData("--api-key", Sentinel, "--nuget-rehearse")]
    [InlineData("--publish", "true", "--nuget-verify-handoff")]
    [InlineData("--policy", "untrusted.json", "--nuget-verify-handoff")]
    [InlineData("--api-key", Sentinel, "--nuget-verify-handoff")]
    [InlineData("--files", "untrusted.nupkg", "--nuget-verify-handoff")]
    public async Task Run_ConsumerOverride_RejectsWithoutEchoingValue(
        string flag,
        string value,
        string mode
    )
    {
        // Arrange
        using var fixture = new RunnerFixture();
        var start = Start(fixture.Root, mode);
        start.ArgumentList.Add(flag);
        start.ArgumentList.Add(value);

        // Act
        var output = await ExecuteAsync(start, TestContext.Current.CancellationToken);

        // Assert
        Assert.Equal(1, output.ExitCode);
        Assert.Contains(NuGetHandoffOptions.Usage, output.Text);
        Assert.DoesNotContain(value, output.Text);
    }

    private static ProcessStartInfo Start(string root, string mode = "--nuget-dry-run")
    {
        if (mode is "--nuget-rehearse" or "--nuget-verify-handoff" or "--nuget-verify-intent")
            return ProcessRunner.CreateStartInfo(
                root,
                [
                    "dotnet",
                    typeof(NuGetRelease).Assembly.Location,
                    .. NuGetHandoffOptionsTests.Arguments(root, mode),
                ]
            );
        var start = ProcessRunner.CreateStartInfo(
            root,
            [
                "dotnet",
                typeof(NuGetRelease).Assembly.Location,
                "--root",
                root,
                mode,
                "--sha",
                MainFixture.Sha,
                "--version",
                "2.2.0",
                "--readiness-run",
                "100",
            ]
        );
        if (mode == "--nuget-prepare")
        {
            start.ArgumentList.Add("--tag");
            start.ArgumentList.Add("v2.2.0");
        }
        return start;
    }

    internal static string RepositoryRoot()
    {
        var directory = new DirectoryInfo(AppContext.BaseDirectory);
        while (
            directory is not null && !File.Exists(Path.Combine(directory.FullName, "global.json"))
        )
            directory = directory.Parent;

        return directory?.FullName
            ?? throw new DirectoryNotFoundException("Repository root not found.");
    }

    private static async Task<CommandOutput> ExecuteAsync(
        ProcessStartInfo start,
        CancellationToken ct
    )
    {
        using var timeout = CancellationTokenSource.CreateLinkedTokenSource(ct);
        timeout.CancelAfter(TimeSpan.FromSeconds(30));
        using var process = new Process { StartInfo = start };
        process.Start();
        var stdout = process.StandardOutput.ReadToEndAsync(timeout.Token);
        var stderr = process.StandardError.ReadToEndAsync(timeout.Token);
        try
        {
            await process.WaitForExitAsync(timeout.Token);
            return new CommandOutput(process.ExitCode, await stdout + await stderr);
        }
        finally
        {
            if (!process.HasExited)
            {
                process.Kill(entireProcessTree: true);
                await process.WaitForExitAsync(CancellationToken.None);
            }
        }
    }
}
