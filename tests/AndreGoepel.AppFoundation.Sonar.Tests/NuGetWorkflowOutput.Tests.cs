using System.Text;
using AndreGoepel.AppFoundation.Sonar;
using AndreGoepel.Core;
using Xunit;

namespace AndreGoepel.AppFoundation.Sonar.Tests;

public sealed class NuGetWorkflowOutputTests
{
    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task Complete_VerifiedPreparation_ExportsOnlyBatchAndOriginLocations(
        bool annotated
    )
    {
        // Arrange
        using var fixture = new OutputFixture();
        await fixture.InitializeAsync(annotated, TestContext.Current.CancellationToken);

        // Act
        var result = await fixture.CompleteAsync(TestContext.Current.CancellationToken);

        // Assert
        Assert.True(result.IsSuccess, result.Error);
        var lines = await File.ReadAllLinesAsync(
            fixture.Path,
            TestContext.Current.CancellationToken
        );
        Assert.Equal(
            [
                "manifest-path=" + fixture.Output.ManifestPath,
                "packages-path="
                    + System.IO.Path.Combine(
                        System.IO.Path.GetDirectoryName(fixture.Output.ManifestPath)!,
                        "packages"
                    ),
                "origin-path=" + fixture.Output.OriginPath,
                "origin-artifact-name=" + fixture.Output.ArtifactName,
                $"batch-artifact-name=nuget-batch-{MainFixture.Sha}-500-1",
            ],
            lines
        );
        Assert.DoesNotContain("reports", string.Join('\n', lines));
        var bytes = await File.ReadAllBytesAsync(
            fixture.Path,
            TestContext.Current.CancellationToken
        );
        Assert.False(bytes.AsSpan().StartsWith(Encoding.UTF8.Preamble));
    }

    [Fact]
    public async Task Complete_FailedPreparation_LeavesRunnerOutputEmpty()
    {
        // Arrange
        using var fixture = new OutputFixture();
        await fixture.InitializeAsync(false, TestContext.Current.CancellationToken);

        // Act
        var result = await NuGetWorkflowOutput.CompleteAsync(
            Result.Fail<NuGetPreparationOutput>("Preparation rejected."),
            fixture.Producer.Context,
            fixture.Environment,
            TestContext.Current.CancellationToken
        );

        // Assert
        Assert.True(result.IsFailure);
        Assert.Empty(
            await File.ReadAllBytesAsync(fixture.Path, TestContext.Current.CancellationToken)
        );
    }

    [Theory]
    [InlineData("outside-temp")]
    [InlineData("relative-output")]
    [InlineData("missing-output")]
    [InlineData("missing-temp")]
    [InlineData("relative-temp")]
    [InlineData("nonempty-output")]
    public async Task Complete_InvalidRunnerOutput_DoesNotOverwriteOrCreateFiles(string fault)
    {
        // Arrange
        using var fixture = new OutputFixture();
        await fixture.InitializeAsync(false, TestContext.Current.CancellationToken);
        if (fault == "outside-temp")
            fixture.Temporary = System.IO.Path.Combine(fixture.Temporary!, "other");
        if (fault == "relative-output")
            fixture.OutputPath = "relative-output";
        if (fault == "missing-output")
            fixture.OutputPath = System.IO.Path.Combine(fixture.Temporary!, "missing");
        if (fault == "missing-temp")
            fixture.Temporary = null;
        if (fault == "relative-temp")
            fixture.Temporary = "relative-temp";
        if (fault == "nonempty-output")
            await File.WriteAllTextAsync(
                fixture.Path,
                "previous=preserved\n",
                TestContext.Current.CancellationToken
            );
        var before = await File.ReadAllBytesAsync(
            fixture.Path,
            TestContext.Current.CancellationToken
        );

        // Act
        var result = await fixture.CompleteAsync(TestContext.Current.CancellationToken);

        // Assert
        Assert.True(result.IsFailure, fault);
        Assert.Equal(
            before,
            await File.ReadAllBytesAsync(fixture.Path, TestContext.Current.CancellationToken)
        );
        Assert.False(
            File.Exists(System.IO.Path.Combine(fixture.Temporary ?? fixture.Runner.Root, "missing"))
        );
    }

    [Theory]
    [InlineData("manifest-name")]
    [InlineData("manifest-outside")]
    [InlineData("manifest-missing")]
    [InlineData("origin-mismatch")]
    [InlineData("origin-missing")]
    [InlineData("artifact-mismatch")]
    [InlineData("artifact-newline")]
    [InlineData("packages-missing")]
    [InlineData("glob-path")]
    public async Task Complete_InvalidPreparedOutput_ExportsNothing(string fault)
    {
        // Arrange
        using var fixture = new OutputFixture();
        await fixture.InitializeAsync(false, TestContext.Current.CancellationToken);
        var output = fixture.Output;
        if (fault == "manifest-name")
            fixture.Output = output with
            {
                ManifestPath = System.IO.Path.Combine(
                    System.IO.Path.GetDirectoryName(output.ManifestPath)!,
                    "other.json"
                ),
            };
        if (fault == "manifest-outside")
            fixture.Output = output with
            {
                ManifestPath = System.IO.Path.Combine(fixture.Runner.Root, "dry-run.json"),
            };
        if (fault == "manifest-missing")
            File.Delete(output.ManifestPath);
        if (fault == "origin-mismatch")
            fixture.Output = output with { OriginPath = output.ManifestPath };
        if (fault == "origin-missing")
            File.Delete(output.OriginPath);
        if (fault is "artifact-mismatch" or "artifact-newline")
            fixture.Output = output with
            {
                ArtifactName =
                    fault == "artifact-newline" ? output.ArtifactName + "\ninjected=true" : "other",
            };
        if (fault == "packages-missing")
            fixture.Output = fixture.EmptyBatch("missing-packages", false);
        if (fault == "glob-path")
            fixture.Output = fixture.EmptyBatch("batch[1]", true);

        // Act
        var result = await fixture.CompleteAsync(TestContext.Current.CancellationToken);

        // Assert
        Assert.True(result.IsFailure, fault);
        Assert.Empty(
            await File.ReadAllBytesAsync(fixture.Path, TestContext.Current.CancellationToken)
        );
    }

    [Fact]
    public async Task Complete_StandaloneInvocation_DoesNotRequireRunnerOutput()
    {
        // Arrange
        using var fixture = new OutputFixture();
        await fixture.InitializeAsync(false, TestContext.Current.CancellationToken);
        fixture.OutputPath = null;

        // Act
        var result = await fixture.CompleteAsync(TestContext.Current.CancellationToken);

        // Assert
        Assert.True(result.IsSuccess, result.Error);
        Assert.Empty(
            await File.ReadAllBytesAsync(fixture.Path, TestContext.Current.CancellationToken)
        );
    }

    [Fact]
    public async Task Complete_Cancellation_ExportsNothing()
    {
        // Arrange
        using var fixture = new OutputFixture();
        await fixture.InitializeAsync(false, TestContext.Current.CancellationToken);
        using var cancellation = new CancellationTokenSource();
        await cancellation.CancelAsync();

        // Act
        var error = await Record.ExceptionAsync(() => fixture.CompleteAsync(cancellation.Token));

        // Assert
        Assert.IsType<OperationCanceledException>(error, exactMatch: false);
        Assert.Empty(
            await File.ReadAllBytesAsync(fixture.Path, TestContext.Current.CancellationToken)
        );
    }

    [Theory]
    [InlineData("output")]
    [InlineData("manifest")]
    [InlineData("origin")]
    [InlineData("packages")]
    public async Task Complete_LinkedLocations_RejectsOnLinux(string target)
    {
        // Arrange
        if (OperatingSystem.IsWindows())
            return;
        using var fixture = new OutputFixture();
        await fixture.InitializeAsync(false, TestContext.Current.CancellationToken);
        if (target == "packages")
        {
            fixture.Output = fixture.EmptyBatch("linked-packages", false);
            Directory.CreateSymbolicLink(
                System.IO.Path.Combine(
                    System.IO.Path.GetDirectoryName(fixture.Output.ManifestPath)!,
                    "packages"
                ),
                fixture.Runner.Root
            );
        }
        else
        {
            var path = target switch
            {
                "output" => fixture.Path,
                "manifest" => fixture.Output.ManifestPath,
                _ => fixture.Output.OriginPath,
            };
            File.Delete(path);
            var destination = System.IO.Path.Combine(fixture.Runner.Root, "link-target");
            await File.WriteAllTextAsync(destination, "", TestContext.Current.CancellationToken);
            File.CreateSymbolicLink(path, destination);
        }

        // Act
        var result = await fixture.CompleteAsync(TestContext.Current.CancellationToken);

        // Assert
        Assert.True(result.IsFailure);
        Assert.Empty(
            await File.ReadAllBytesAsync(fixture.Path, TestContext.Current.CancellationToken)
        );
    }

    private sealed class OutputFixture : IDisposable
    {
        internal NuGetPreparationTests.PreparationFixture Producer { get; } = new();
        internal RunnerFixture Runner { get; } = new();
        internal NuGetPreparationOutput Output { get; set; } = null!;
        internal string Path => System.IO.Path.Combine(Runner.Root, "step-output");
        internal string? OutputPath { get; set; }
        internal string? Temporary { get; set; }

        internal async Task InitializeAsync(bool annotated, CancellationToken ct)
        {
            await Producer.InitializeAsync(annotated, ct);
            var result = await Producer.RunAsync(ct);
            Assert.True(result.IsSuccess, result.Error);
            Output = result.Value!;
            OutputPath = Path;
            Temporary = Runner.Root;
            await File.WriteAllTextAsync(Path, "", ct);
        }

        internal string? Environment(string name) =>
            name switch
            {
                "GITHUB_OUTPUT" => OutputPath,
                "RUNNER_TEMP" => Temporary,
                _ => null,
            };

        internal Task<Result> CompleteAsync(CancellationToken ct) =>
            NuGetWorkflowOutput.CompleteAsync(Result.Ok(Output), Producer.Context, Environment, ct);

        internal NuGetPreparationOutput EmptyBatch(string name, bool packages)
        {
            var directory = System.IO.Path.Combine(Producer.Release.Root, "artifacts/sonar", name);
            Directory.CreateDirectory(System.IO.Path.Combine(directory, "origin"));
            if (packages)
                Directory.CreateDirectory(System.IO.Path.Combine(directory, "packages"));
            var manifest = System.IO.Path.Combine(directory, "dry-run.json");
            var origin = System.IO.Path.Combine(directory, "origin", "origin.json");
            File.WriteAllText(manifest, "{}");
            File.WriteAllText(origin, "{}");
            return Output with { ManifestPath = manifest, OriginPath = origin };
        }

        public void Dispose()
        {
            Producer.Dispose();
            Runner.Dispose();
        }
    }
}
