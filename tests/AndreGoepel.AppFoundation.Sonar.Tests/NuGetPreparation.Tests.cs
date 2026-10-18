using System.Text.Json;
using AndreGoepel.AppFoundation.Sonar;
using AndreGoepel.Core;
using Xunit;

namespace AndreGoepel.AppFoundation.Sonar.Tests;

public sealed class NuGetPreparationTests
{
    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task Run_VerifiedBatch_WritesOriginBoundToCapturedEvent(bool annotated)
    {
        // Arrange
        using var fixture = new PreparationFixture();
        await fixture.InitializeAsync(annotated, TestContext.Current.CancellationToken);

        // Act
        var result = await fixture.RunAsync(TestContext.Current.CancellationToken);

        // Assert
        Assert.True(result.IsSuccess, result.Error);
        Assert.Equal(1, fixture.Builds);
        Assert.Equal(4, fixture.Checks);
        var output = result.Value!;
        var origin = JsonSerializer.Deserialize<NuGetPreparationOrigin>(
            await File.ReadAllBytesAsync(output.OriginPath, TestContext.Current.CancellationToken),
            JsonFormat.Options
        )!;
        Assert.Equal(
            new NuGetPreparationOrigin(
                1,
                MainPolicy.Repository,
                fixture.Release.Sha,
                fixture.Release.Tag!,
                fixture.Context.TagObjectSha,
                500,
                1,
                fixture.Release.ReadinessRunId,
                SourceFiles.Hash(
                    await File.ReadAllBytesAsync(
                        output.ManifestPath,
                        TestContext.Current.CancellationToken
                    )
                ),
                fixture.Clock.GetUtcNow(),
                true,
                false
            ),
            origin
        );
        Assert.Equal($"nuget-origin-{fixture.Release.Sha}-500-1", output.ArtifactName);
        Assert.Equal(12, Directory.GetFiles(Path.Combine(fixture.Batch!, "packages")).Length);
        Assert.Single(Directory.GetFiles(Path.GetDirectoryName(output.OriginPath)!));
        Assert.True(output.DryRun);
        Assert.False(output.ReleaseEligible);
    }

    [Theory]
    [InlineData("retargeted")]
    [InlineData("peeled-event")]
    [InlineData("gate")]
    public async Task Run_InvalidInitialEvidence_StopsBeforePacking(string fault)
    {
        // Arrange
        using var fixture = new PreparationFixture();
        await fixture.InitializeAsync(annotated: true, TestContext.Current.CancellationToken);
        if (fault == "retargeted")
            fixture.Retarget();
        else if (fault == "peeled-event")
            fixture.Context = fixture.Context with { TagObjectSha = fixture.Release.Sha };
        else
            fixture.Evidence.Main.Responses["qualitygates/project_status"]["projectStatus"]![
                "status"
            ] = "ERROR";

        // Act
        var result = await fixture.RunAsync(TestContext.Current.CancellationToken);

        // Assert
        Assert.True(result.IsFailure, fault);
        Assert.Equal(0, fixture.Builds);
        Assert.Empty(fixture.Origins());
    }

    [Fact]
    public async Task Run_TagRetargetedDuringPacking_PreservesOriginalIdentityAndRejects()
    {
        // Arrange
        using var fixture = new PreparationFixture();
        await fixture.InitializeAsync(annotated: true, TestContext.Current.CancellationToken);
        fixture.OnBuilt = fixture.Retarget;

        // Act
        var result = await fixture.RunAsync(TestContext.Current.CancellationToken);

        // Assert
        Assert.True(result.IsFailure);
        Assert.Equal(1, fixture.Builds);
        Assert.Equal(NuGetHandoffTests.HandoffFixture.TagObject, fixture.Context.TagObjectSha);
        Assert.Empty(fixture.Origins());
    }

    [Theory]
    [InlineData("main")]
    [InlineData("gate")]
    [InlineData("expired")]
    [InlineData("package")]
    [InlineData("symbol")]
    [InlineData("manifest")]
    [InlineData("tag")]
    public async Task Run_EvidenceChangesDuringFinalHandoff_DoesNotWriteOrigin(string fault)
    {
        // Arrange
        using var fixture = new PreparationFixture();
        await fixture.InitializeAsync(annotated: true, TestContext.Current.CancellationToken);
        fixture.OnVerify = () =>
        {
            if (fixture.Checks != 4)
                return;
            switch (fault)
            {
                case "main":
                    fixture.Evidence.Main.Responses["git/ref/heads/main"]["object"]!["sha"] =
                        new string('b', 40);
                    break;
                case "gate":
                    fixture.Evidence.Main.Responses["qualitygates/project_status"][
                        "projectStatus"
                    ]!["status"] = "ERROR";
                    break;
                case "expired":
                    fixture.Clock.Now = fixture.Clock.Now.AddHours(1);
                    break;
                case "tag":
                    fixture.Retarget();
                    break;
                case "manifest":
                    File.AppendAllText(Path.Combine(fixture.Batch!, "dry-run.json"), " ");
                    break;
                default:
                    var extension = fault == "package" ? "*.nupkg" : "*.snupkg";
                    File.AppendAllText(
                        Directory.GetFiles(Path.Combine(fixture.Batch!, "packages"), extension)[0],
                        "tampered"
                    );
                    break;
            }
        };

        // Act
        var result = await fixture.RunAsync(TestContext.Current.CancellationToken);

        // Assert
        Assert.True(result.IsFailure, fault);
        Assert.Empty(fixture.Origins());
    }

    [Fact]
    public async Task Run_PreexistingOriginDirectory_RejectsWithoutOverwriting()
    {
        // Arrange
        using var fixture = new PreparationFixture();
        await fixture.InitializeAsync(annotated: false, TestContext.Current.CancellationToken);
        fixture.OnBuilt = () =>
        {
            Directory.CreateDirectory(Path.Combine(fixture.Batch!, "origin"));
            File.WriteAllText(Path.Combine(fixture.Batch!, "origin/origin.json"), "existing");
        };

        // Act
        var result = await fixture.RunAsync(TestContext.Current.CancellationToken);

        // Assert
        Assert.True(result.IsFailure);
        Assert.Equal(
            "existing",
            await File.ReadAllTextAsync(
                Assert.Single(fixture.Origins()),
                TestContext.Current.CancellationToken
            )
        );
    }

    [Fact]
    public async Task Run_CancelledDuringFinalHandoff_DoesNotWriteOrigin()
    {
        // Arrange
        using var fixture = new PreparationFixture();
        await fixture.InitializeAsync(annotated: false, TestContext.Current.CancellationToken);
        using var cancellation = CancellationTokenSource.CreateLinkedTokenSource(
            TestContext.Current.CancellationToken
        );
        fixture.OnVerify = () =>
        {
            if (fixture.Checks == 4)
                cancellation.Cancel();
        };

        // Act
        await Assert.ThrowsAnyAsync<OperationCanceledException>(() =>
            fixture.RunAsync(cancellation.Token)
        );

        // Assert
        Assert.Empty(fixture.Origins());
    }

    internal sealed class PreparationFixture : IReleaseReadiness, INuGetPackageBuilder, IDisposable
    {
        internal ReleaseFixture Evidence { get; } = new();
        internal NuGetRelease Release => Evidence.Release with { Tag = "v2.2.0" };
        internal NuGetPreparationContext Context { get; set; } = null!;
        internal NuGetAcceptanceFixture.AcceptanceClock Clock { get; } = new(DateTimeOffset.UtcNow);
        internal int Builds { get; private set; }
        internal int Checks { get; private set; }
        internal string? Batch { get; private set; }
        internal Action? OnBuilt { get; set; }
        internal Action? OnVerify { get; set; }

        internal async Task InitializeAsync(bool annotated, CancellationToken ct)
        {
            await Evidence.InitializeAsync(ct);
            var environment = NuGetPreparationContextTests.EnvironmentFor(Release);
            var captured = NuGetPreparationContext.Capture(
                Release,
                environment.GetValueOrDefault,
                NuGetPreparationContextTests.Element(
                    NuGetPreparationContextTests.Payload(annotated)
                )
            );
            Assert.True(captured.IsSuccess, captured.Error);
            Context = captured.Value!;
            Evidence.Main.Responses["git/ref/tags/v2.2.0"] = MainFixture.Node(
                new
                {
                    @object = new
                    {
                        type = annotated ? "tag" : "commit",
                        sha = Context.TagObjectSha,
                    },
                }
            );
            Evidence.Main.Responses["git/tags/" + Context.TagObjectSha] = MainFixture.Node(
                new { @object = new { type = "commit", sha = Release.Sha } }
            );
        }

        internal void Retarget()
        {
            var replacement = new string('b', 40);
            Evidence.Main.Responses["git/ref/tags/v2.2.0"]["object"]!["sha"] = replacement;
            Evidence.Main.Responses["git/tags/" + replacement] = MainFixture.Node(
                new { @object = new { type = "commit", sha = Release.Sha } }
            );
        }

        internal Task<Result<NuGetPreparationOutput>> RunAsync(CancellationToken ct) =>
            new NuGetPreparation(
                new NuGetDryRun(this, this),
                new NuGetHandoff(this, Evidence.Main, Clock),
                Evidence.Main,
                Clock
            ).RunAsync(Context, ct);

        public Task<Result<ReadinessReceipt>> VerifyAsync(
            NuGetRelease release,
            CancellationToken ct
        )
        {
            Checks++;
            OnVerify?.Invoke();
            return Evidence.Verifier().VerifyAsync(release, ct);
        }

        public async Task<Result<PackageEvidence[]>> BuildAsync(
            NuGetRelease release,
            string directory,
            CancellationToken ct
        )
        {
            Builds++;
            Batch = directory;
            var result = await new NuGetPackageBuilder(
                new NuGetPackageBuilderTests.BuildRunner(release, ""),
                Evidence.Contract
            ).BuildAsync(release, directory, ct);
            OnBuilt?.Invoke();
            return result;
        }

        internal string[] Origins() =>
            Directory.Exists(Path.Combine(Evidence.Root, "artifacts"))
                ? Directory.GetFiles(
                    Path.Combine(Evidence.Root, "artifacts"),
                    "origin.json",
                    SearchOption.AllDirectories
                )
                : [];

        public void Dispose() => Evidence.Dispose();
    }
}
