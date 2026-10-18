using AndreGoepel.AppFoundation.Sonar;
using AndreGoepel.Core;
using Xunit;

namespace AndreGoepel.AppFoundation.Sonar.Tests;

public sealed class NuGetPublicationRehearsalTests
{
    [Fact]
    public async Task Run_CompleteBatch_VerifiesBeforeLoginAndEverySimulatedUpload()
    {
        // Arrange
        using var fixture = new RehearsalFixture();
        await fixture.InitializeAsync(TestContext.Current.CancellationToken);

        // Act
        var result = await fixture.RunAsync(TestContext.Current.CancellationToken);

        // Assert
        Assert.True(result.IsSuccess, result.Error);
        Assert.Equal(12, result.Value!.Accepted.Length);
        Assert.Equal(12, fixture.Sink.Accepted.Count);
        Assert.True(result.Value.DryRun);
        Assert.False(result.Value.ReleaseEligible);
        Assert.Equal(4, fixture.Sink.ChecksAtLogin);
        Assert.Equal(Enumerable.Range(0, 12).Select(i => 6 + i * 2), fixture.Sink.ChecksAtUpload);
        Assert.Equal("login", fixture.Sink.Events[0]);
        Assert.Equal(13, fixture.Sink.Events.Count);
        Assert.All(
            fixture.Sink.Accepted,
            upload => Assert.Equal(upload.Package.Sha256, upload.Hash)
        );
    }

    [Theory]
    [InlineData("gate")]
    [InlineData("main")]
    [InlineData("tag")]
    [InlineData("manifest")]
    [InlineData("package")]
    [InlineData("symbol")]
    [InlineData("expired")]
    [InlineData("missing-e2e")]
    [InlineData("unknown-workflow")]
    public async Task Run_InvalidEvidence_NeverCallsLoginOrUpload(string fault)
    {
        // Arrange
        using var fixture = new RehearsalFixture();
        await fixture.InitializeAsync(TestContext.Current.CancellationToken);
        fixture.Invalidate(fault);

        // Act
        var result = await fixture.RunAsync(TestContext.Current.CancellationToken);

        // Assert
        Assert.True(result.IsFailure, fault);
        Assert.Empty(fixture.Sink.Events);
        Assert.Empty(fixture.Sink.Accepted);
    }

    [Theory]
    [InlineData("empty")]
    [InlineData("duplicate")]
    [InlineData("unknown")]
    [InlineData("traversal")]
    public async Task Run_InvalidRetrySelection_NeverCallsLoginOrUpload(string fault)
    {
        // Arrange
        using var fixture = new RehearsalFixture();
        await fixture.InitializeAsync(TestContext.Current.CancellationToken);
        string[] selection = fault switch
        {
            "empty" => [],
            "duplicate" => [fixture.Files[0], fixture.Files[0]],
            "unknown" => ["Unknown.Package.2.2.0.nupkg"],
            _ => ["../" + fixture.Files[0]],
        };

        // Act
        var result = await fixture.RunAsync(TestContext.Current.CancellationToken, selection);

        // Assert
        Assert.True(result.IsFailure, fault);
        Assert.Empty(fixture.Sink.Events);
    }

    [Theory]
    [InlineData("gate")]
    [InlineData("tag")]
    [InlineData("package")]
    [InlineData("expired")]
    public async Task Run_EvidenceChangesAfterSnapshot_BlocksBeforeLogin(string fault)
    {
        // Arrange
        using var fixture = new RehearsalFixture();
        await fixture.InitializeAsync(TestContext.Current.CancellationToken);
        fixture.Handoff.OnVerify = () =>
        {
            if (fixture.Handoff.Checks == 3)
                fixture.Invalidate(fault);
        };

        // Act
        var result = await fixture.RunAsync(TestContext.Current.CancellationToken);

        // Assert
        Assert.True(result.IsFailure, fault);
        Assert.Empty(fixture.Sink.Events);
    }

    [Theory]
    [InlineData("gate")]
    [InlineData("main")]
    [InlineData("tag")]
    [InlineData("manifest")]
    [InlineData("symbol")]
    [InlineData("expired")]
    public async Task Run_EvidenceChangesDuringLogin_BlocksBeforeFirstUpload(string fault)
    {
        // Arrange
        using var fixture = new RehearsalFixture();
        await fixture.InitializeAsync(TestContext.Current.CancellationToken);
        fixture.Sink.OnLogin = () => fixture.Invalidate(fault);

        // Act
        var result = await fixture.RunAsync(TestContext.Current.CancellationToken);

        // Assert
        Assert.True(result.IsFailure, fault);
        Assert.Equal(["login"], fixture.Sink.Events);
        Assert.Empty(fixture.Sink.Accepted);
    }

    [Theory]
    [InlineData("gate")]
    [InlineData("tag")]
    [InlineData("symbol")]
    [InlineData("expired")]
    public async Task Run_EvidenceChangesAfterOneUpload_StopsRemainingBatch(string fault)
    {
        // Arrange
        using var fixture = new RehearsalFixture();
        await fixture.InitializeAsync(TestContext.Current.CancellationToken);
        fixture.Sink.OnUpload = _ => fixture.Invalidate(fault);

        // Act
        var result = await fixture.RunAsync(TestContext.Current.CancellationToken);

        // Assert
        Assert.True(result.IsFailure, fault);
        Assert.Single(fixture.Sink.Accepted);
        Assert.Equal(2, fixture.Sink.Events.Count);
    }

    [Fact]
    public async Task Run_SourceFileReplacedAtSink_SuppliesOriginalVerifiedBytes()
    {
        // Arrange
        using var fixture = new RehearsalFixture();
        await fixture.InitializeAsync(TestContext.Current.CancellationToken);
        var file = fixture.Files[0];
        fixture.Sink.OnUpload = package =>
            File.WriteAllText(
                Path.Combine(fixture.Handoff.Input.Directory, "packages", package.File),
                "replacement"
            );

        // Act
        var result = await fixture.RunAsync(TestContext.Current.CancellationToken, [file]);

        // Assert
        Assert.True(result.IsSuccess, result.Error);
        var upload = Assert.Single(fixture.Sink.Accepted);
        Assert.Equal(upload.Package.Sha256, upload.Hash);
        Assert.NotEqual(SourceFiles.Hash("replacement"u8.ToArray()), upload.Hash);
        Assert.False(fixture.Sink.PayloadWritable);
    }

    [Fact]
    public async Task Run_LoginFailure_StopsWithoutUploadOrAutomaticRetry()
    {
        // Arrange
        using var fixture = new RehearsalFixture();
        await fixture.InitializeAsync(TestContext.Current.CancellationToken);
        fixture.Sink.FailLogin = true;

        // Act
        var result = await fixture.RunAsync(TestContext.Current.CancellationToken);

        // Assert
        Assert.True(result.IsFailure);
        Assert.Equal(["login"], fixture.Sink.Events);
        Assert.Empty(fixture.Sink.Accepted);
    }

    [Fact]
    public async Task Run_PartialFailure_RetryRevalidatesAndSendsOnlyRemainingIdenticalFiles()
    {
        // Arrange
        using var fixture = new RehearsalFixture();
        await fixture.InitializeAsync(TestContext.Current.CancellationToken);
        fixture.Sink.FailUpload = 3;
        var first = await fixture.RunAsync(TestContext.Current.CancellationToken);
        Assert.True(first.IsFailure);
        Assert.Equal(2, fixture.Sink.Accepted.Count);
        Assert.Equal(4, fixture.Sink.Events.Count);
        var accepted = fixture.Sink.Accepted.Select(a => a.Package.File).ToArray();
        var remaining = fixture.Files.Except(accepted, StringComparer.Ordinal).ToArray();
        var checks = fixture.Handoff.Checks;
        fixture.Sink.Reset();

        // Act
        var retry = await fixture.RunAsync(TestContext.Current.CancellationToken, remaining);

        // Assert
        Assert.True(retry.IsSuccess, retry.Error);
        Assert.Equal(10, retry.Value!.Accepted.Length);
        Assert.Equal(checks + 4, fixture.Sink.ChecksAtLogin);
        Assert.Equal(remaining, fixture.Sink.Accepted.Select(a => a.Package.File));
        Assert.All(
            fixture.Sink.Accepted,
            upload => Assert.Equal(upload.Package.Sha256, upload.Hash)
        );
    }

    [Theory]
    [InlineData("expired")]
    [InlineData("tag")]
    [InlineData("package")]
    public async Task Run_PartialFailureAndChangedEvidence_RejectsRetryBeforeLogin(string fault)
    {
        // Arrange
        using var fixture = new RehearsalFixture();
        await fixture.InitializeAsync(TestContext.Current.CancellationToken);
        fixture.Sink.FailUpload = 2;
        var first = await fixture.RunAsync(TestContext.Current.CancellationToken);
        Assert.True(first.IsFailure);
        fixture.Invalidate(fault);
        fixture.Sink.Reset();

        // Act
        var retry = await fixture.RunAsync(
            TestContext.Current.CancellationToken,
            fixture.Files[1..]
        );

        // Assert
        Assert.True(retry.IsFailure, fault);
        Assert.Empty(fixture.Sink.Events);
    }

    [Fact]
    public async Task Run_SymbolOnlyRetry_RevalidatesCompleteBatchAndSendsOnlySymbols()
    {
        // Arrange
        using var fixture = new RehearsalFixture();
        await fixture.InitializeAsync(TestContext.Current.CancellationToken);
        var symbols = fixture
            .Files.Where(f => f.EndsWith(".snupkg", StringComparison.Ordinal))
            .ToArray();

        // Act
        var result = await fixture.RunAsync(TestContext.Current.CancellationToken, symbols);

        // Assert
        Assert.True(result.IsSuccess, result.Error);
        Assert.Equal(6, fixture.Sink.Accepted.Count);
        Assert.Equal(symbols, fixture.Sink.Accepted.Select(a => a.Package.File));
        Assert.Equal(4, fixture.Sink.ChecksAtLogin);
    }

    [Fact]
    public async Task Run_SymbolOnlyRetryWithMissingPackage_RejectsBeforeLogin()
    {
        // Arrange
        using var fixture = new RehearsalFixture();
        await fixture.InitializeAsync(TestContext.Current.CancellationToken);
        var symbols = fixture
            .Files.Where(f => f.EndsWith(".snupkg", StringComparison.Ordinal))
            .ToArray();
        var package = fixture.Files.First(f => f.EndsWith(".nupkg", StringComparison.Ordinal));
        File.Delete(Path.Combine(fixture.Handoff.Input.Directory, "packages", package));

        // Act
        var result = await fixture.RunAsync(TestContext.Current.CancellationToken, symbols);

        // Assert
        Assert.True(result.IsFailure);
        Assert.Empty(fixture.Sink.Events);
    }

    [Fact]
    public async Task Run_CancelledDuringLogin_PropagatesWithoutUploading()
    {
        // Arrange
        using var fixture = new RehearsalFixture();
        await fixture.InitializeAsync(TestContext.Current.CancellationToken);
        using var cancellation = CancellationTokenSource.CreateLinkedTokenSource(
            TestContext.Current.CancellationToken
        );
        fixture.Sink.OnLogin = cancellation.Cancel;

        // Act
        var error = await Record.ExceptionAsync(() => fixture.RunAsync(cancellation.Token));

        // Assert
        Assert.IsType<OperationCanceledException>(error, exactMatch: false);
        Assert.Equal(["login"], fixture.Sink.Events);
        Assert.Empty(fixture.Sink.Accepted);
    }

    [Fact]
    public async Task Run_UnknownUploadOutcome_DoesNotAutomaticallyRetry()
    {
        // Arrange
        using var fixture = new RehearsalFixture();
        await fixture.InitializeAsync(TestContext.Current.CancellationToken);
        fixture.Sink.OnUpload = _ => throw new IOException("Simulated lost response");

        // Act
        var error = await Record.ExceptionAsync(() =>
            fixture.RunAsync(TestContext.Current.CancellationToken)
        );

        // Assert
        Assert.IsType<IOException>(error);
        Assert.Equal(2, fixture.Sink.Events.Count);
        Assert.Empty(fixture.Sink.Accepted);
    }

    private sealed class RehearsalFixture : IDisposable
    {
        internal NuGetHandoffTests.HandoffFixture Handoff { get; } = new();
        internal RecordingSink Sink { get; }
        internal string[] Files { get; private set; } = [];

        internal RehearsalFixture() => Sink = new(() => Handoff.Checks);

        internal async Task InitializeAsync(CancellationToken ct)
        {
            await Handoff.InitializeAsync(annotated: true, ct);
            Files = Directory
                .GetFiles(Path.Combine(Handoff.Input.Directory, "packages"))
                .Select(path => Path.GetFileName(path))
                .Order(StringComparer.Ordinal)
                .ToArray();
        }

        internal Task<Result<NuGetRehearsalReceipt>> RunAsync(
            CancellationToken ct,
            string[]? files = null
        ) =>
            new NuGetPublicationRehearsal(
                new NuGetHandoff(Handoff, Handoff.Evidence.Main, Handoff.Clock),
                Sink
            ).RunAsync(Handoff.Input, files, ct);

        internal void Invalidate(string fault)
        {
            switch (fault)
            {
                case "gate":
                    Handoff.Evidence.Main.Responses["qualitygates/project_status"][
                        "projectStatus"
                    ]!["status"] = "ERROR";
                    break;
                case "main":
                    Handoff.Evidence.Main.Responses["git/ref/heads/main"]["object"]!["sha"] =
                        new string('f', 40);
                    break;
                case "tag":
                    Handoff.Retarget();
                    break;
                case "manifest":
                    File.AppendAllText(Handoff.ManifestPath, " ");
                    break;
                case "expired":
                    Handoff.Clock.Now = Handoff.Evidence.Main.Now.AddHours(1);
                    break;
                case "missing-e2e":
                    Handoff.Evidence.Main.Responses["actions/runs/300"]["conclusion"] = "failure";
                    break;
                case "unknown-workflow":
                    Handoff.Evidence.Producer["workflow_id"] = 999;
                    break;
                default:
                    Handoff.ChangeFiles(fault);
                    break;
            }
        }

        public void Dispose() => Handoff.Dispose();
    }

    internal sealed class RecordingSink(Func<int> checks) : INuGetRehearsalSink
    {
        internal List<string> Events { get; } = [];
        internal List<(PackageEvidence Package, string Hash)> Accepted { get; } = [];
        internal List<int> ChecksAtUpload { get; } = [];
        internal int ChecksAtLogin { get; private set; }
        internal bool PayloadWritable { get; private set; }
        internal bool FailLogin { get; set; }
        internal int FailUpload { get; set; }
        internal Action? OnLogin { get; set; }
        internal Action<PackageEvidence>? OnUpload { get; set; }

        public Task<Result> SimulateLoginAsync(CancellationToken ct)
        {
            ct.ThrowIfCancellationRequested();
            Events.Add("login");
            ChecksAtLogin = checks();
            OnLogin?.Invoke();
            return Task.FromResult(
                FailLogin ? Result.Fail("Simulated login failure") : Result.Ok()
            );
        }

        public async Task<Result> SimulateUploadAsync(
            PackageEvidence package,
            Stream bytes,
            CancellationToken ct
        )
        {
            ct.ThrowIfCancellationRequested();
            Events.Add("upload:" + package.File);
            ChecksAtUpload.Add(checks());
            OnUpload?.Invoke(package);
            if (ChecksAtUpload.Count == FailUpload)
                return Result.Fail("Simulated upload failure");

            PayloadWritable |= bytes.CanWrite;
            using var buffer = new MemoryStream();
            await bytes.CopyToAsync(buffer, ct);
            Accepted.Add((package, SourceFiles.Hash(buffer.ToArray())));
            return Result.Ok();
        }

        internal void Reset()
        {
            Events.Clear();
            Accepted.Clear();
            ChecksAtUpload.Clear();
            FailUpload = 0;
        }
    }
}
