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
    [InlineData("ACTIONS_ID_TOKEN_REQUEST_TOKEN")]
    [InlineData("ACTIONS_ID_TOKEN_REQUEST_URL")]
    [InlineData("NUGET_API_KEY")]
    [InlineData("NUGET_AUTH_TOKEN")]
    [InlineData("NUGET_SYMBOL_API_KEY")]
    public async Task Run_PublicationCredential_ExitsBeforeReadingConfiguration(string name)
    {
        // Arrange
        using var fixture = new RunnerFixture();
        var start = Start(fixture.Root);
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
    [InlineData("ACTIONS_ID_TOKEN_REQUEST_TOKEN")]
    [InlineData("ACTIONS_ID_TOKEN_REQUEST_URL")]
    [InlineData("NUGET_API_KEY")]
    [InlineData("NUGET_AUTH_TOKEN")]
    [InlineData("NUGET_SYMBOL_API_KEY")]
    public async Task Bootstrap_PublicationCredential_ExitsBeforeRestoreOrBuild(string name)
    {
        // Arrange
        using var fixture = new RunnerFixture();
        var script = OperatingSystem.IsWindows() ? "analyze.ps1" : "analyze.sh";
        var copy = Path.Combine(fixture.Root, "scripts/sonar", script);
        File.Copy(Path.Combine(RepositoryRoot(), "scripts/sonar", script), copy);
        string[] command = OperatingSystem.IsWindows()
            ? ["pwsh", "-NoProfile", "-File", copy, "--nuget-dry-run"]
            : ["bash", copy, "--nuget-dry-run"];
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

    [Fact]
    public async Task Run_MissingReadCredentials_ExitsWithoutPreparingPackages()
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
        var start = Start(fixture.Root);

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

    private static ProcessStartInfo Start(string root) =>
        ProcessRunner.CreateStartInfo(
            root,
            [
                "dotnet",
                typeof(NuGetRelease).Assembly.Location,
                "--root",
                root,
                "--nuget-dry-run",
                "--sha",
                MainFixture.Sha,
                "--version",
                "2.2.0",
                "--readiness-run",
                "100",
            ]
        );

    private static string RepositoryRoot()
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
