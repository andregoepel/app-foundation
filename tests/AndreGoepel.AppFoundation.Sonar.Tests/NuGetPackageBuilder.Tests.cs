using System.Text.Json;
using AndreGoepel.AppFoundation.Sonar;
using AndreGoepel.Core;
using Xunit;

namespace AndreGoepel.AppFoundation.Sonar.Tests;

public sealed class NuGetPackageBuilderTests
{
    [Theory]
    [InlineData("")]
    [InlineData("clean")]
    [InlineData("restore")]
    [InlineData("version-restore")]
    [InlineData("compile-inputs")]
    [InlineData("changed-inputs")]
    [InlineData("build")]
    [InlineData("audit")]
    [InlineData("vulnerability")]
    [InlineData("discovery")]
    [InlineData("missing-tests")]
    [InlineData("test")]
    [InlineData("skipped-test")]
    [InlineData("pack")]
    public async Task Build_VersionedBuildAndTests_MustPassBeforePacking(string fault)
    {
        // Arrange
        using var fixture = new ReleaseFixture();
        await fixture.InitializeAsync(TestContext.Current.CancellationToken);
        var runner = new BuildRunner(fixture.Release, fault);
        var output = Path.Combine(fixture.Root, "artifacts/dry-run");

        // Act
        var result = await new NuGetPackageBuilder(runner, fixture.Contract).BuildAsync(
            fixture.Release,
            output,
            TestContext.Current.CancellationToken
        );

        // Assert
        Assert.Equal(fault.Length == 0, result.IsSuccess);
        if (fault.Length == 0)
        {
            Assert.Equal(12, result.Value!.Length);
            Assert.Equal(6, runner.Commands.Count(args => args[1] == "pack"));
            Assert.All(
                runner.Commands.Where(args => args[1] == "pack"),
                args =>
                {
                    Assert.Contains("--no-build", args);
                    Assert.Contains("--no-restore", args);
                    Assert.Contains("-p:Version=2.2.0", args);
                    Assert.Contains("-p:RepositoryCommit=" + MainFixture.Sha, args);
                }
            );
        }
        else if (fault != "pack")
            Assert.DoesNotContain(runner.Commands, args => args[1] == "pack");
        Assert.DoesNotContain(
            runner.Commands,
            args => args.Contains("push") || args.Contains("login")
        );
    }

    private sealed class BuildRunner(NuGetRelease release, string fault) : IProcessRunner
    {
        private static readonly string[] Vulnerabilities = ["CVE"];

        internal List<string[]> Commands { get; } = [];

        public async Task<Result<CommandOutput>> RunAsync(
            string root,
            string[] args,
            CancellationToken ct
        )
        {
            Commands.Add(args);
            var stage = args[1];
            var versioned = args.Any(a => a.StartsWith("-p:Version=", StringComparison.Ordinal));
            var discovery = args.Contains("--list-tests");
            var label = stage switch
            {
                "restore" when versioned => "version-restore",
                "msbuild" => "compile-inputs",
                "list" => "audit",
                "test" when discovery => "discovery",
                _ => stage,
            };
            if (label == fault)
                return Result.Ok(new CommandOutput(1, "Failed"));

            var text = "";
            switch (stage)
            {
                case "msbuild":
                    text =
                        versioned && fault == "changed-inputs"
                            ? "{\"DefineConstants\":\"NEW_BEHAVIOR\"}"
                            : "{}";
                    break;
                case "list":
                    text = JsonSerializer.Serialize(
                        new
                        {
                            projects = new[]
                            {
                                new
                                {
                                    frameworks = new[]
                                    {
                                        new
                                        {
                                            topLevelPackages = new[]
                                            {
                                                new
                                                {
                                                    vulnerabilities = fault == "vulnerability"
                                                        ? Vulnerabilities
                                                        : [],
                                                },
                                            },
                                        },
                                    },
                                },
                            },
                        }
                    );
                    break;
                case "test" when discovery:
                    text =
                        fault == "missing-tests"
                            ? "Discovered 0 tests.\n"
                            : "  AndreGoepel.Fixture.Test\nDiscovered 1 tests.\n";
                    break;
                case "test":
                    var directory = args[Array.IndexOf(args, "--results-directory") + 1];
                    await File.WriteAllTextAsync(
                        Path.Combine(directory, "tests.xml"),
                        RunnerFixture
                            .Tests(outcome: fault == "skipped-test" ? "Skip" : "Pass")
                            .ToString(),
                        ct
                    );
                    File.SetLastWriteTimeUtc(
                        Path.Combine(directory, "tests.xml"),
                        DateTime.UtcNow.AddSeconds(1)
                    );
                    break;
                case "pack":
                    var packages = args[Array.IndexOf(args, "-o") + 1];
                    if (Directory.GetFiles(packages).Length == 0)
                        NuGetPackagesTests.CreatePackages(release, packages);
                    break;
            }
            return Result.Ok(new CommandOutput(0, text));
        }
    }
}
