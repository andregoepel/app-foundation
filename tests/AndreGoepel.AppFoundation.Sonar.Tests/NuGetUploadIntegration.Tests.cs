using System.Net;
using AndreGoepel.AppFoundation.Sonar;
using AndreGoepel.Core;
using Xunit;

namespace AndreGoepel.AppFoundation.Sonar.Tests;

public sealed class NuGetUploadIntegrationTests
{
    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task Run_VerifiedBatch_RechecksBeforeSyntheticLoginAndEachOfficialClientRequest(
        bool symbolsOnly
    )
    {
        // Arrange
        using var fixture = new NuGetProvenanceTests.ProvenanceFixture();
        await fixture.InitializeAsync(TestContext.Current.CancellationToken);
        using var sink = new OfflineUploadSink(fixture);
        var selected = symbolsOnly ? new[] { "AndreGoepel.AppFoundation.2.2.0.snupkg" } : null;

        // Act
        var result = await fixture.RunAsync(TestContext.Current.CancellationToken, sink, selected);

        // Assert
        Assert.True(result.IsSuccess, result.Error);
        Assert.Equal(symbolsOnly ? 1 : 12, sink.Attempts.Count);
        Assert.Equal(result.Value!.Accepted, sink.Attempts.Select(a => a.Package));
        Assert.All(sink.Attempts, a => Assert.Equal(NuGetUploadOutcome.Accepted, a.Outcome));
        Assert.Equal(1, sink.Logins);
        Assert.True(sink.ChecksAtLogin >= 4);
        Assert.Equal(
            Enumerable.Range(1, sink.Attempts.Count).Select(i => sink.ChecksAtLogin + 2 * i),
            sink.ChecksAtRequest
        );
        Assert.All(sink.Requests, r => Assert.Equal(r.Package.Sha256, r.Hash));
        Assert.Equal(
            symbolsOnly ? 1 : 6,
            sink.Requests.Count(r => r.Endpoint == NuGetUploadAdapter.SymbolEndpoint)
        );
        Assert.Equal(
            symbolsOnly ? 0 : 6,
            sink.Requests.Count(r => r.Endpoint == NuGetUploadAdapter.PackageEndpoint)
        );
        Assert.True(result.Value.DryRun);
        Assert.False(result.Value.ReleaseEligible);
    }

    [Theory]
    [InlineData("gate")]
    [InlineData("package")]
    [InlineData("new-attempt")]
    public async Task Run_InvalidEvidence_StopsBeforeSyntheticCredentialAndTransport(string fault)
    {
        // Arrange
        using var fixture = new NuGetProvenanceTests.ProvenanceFixture();
        await fixture.InitializeAsync(TestContext.Current.CancellationToken);
        using var sink = new OfflineUploadSink(fixture);
        Invalidate(fixture, fault);

        // Act
        var result = await fixture.RunAsync(TestContext.Current.CancellationToken, sink);

        // Assert
        Assert.True(result.IsFailure, fault);
        Assert.Equal(0, sink.Logins);
        Assert.Equal(0, sink.Transports);
        Assert.Empty(sink.Requests);
    }

    [Theory]
    [InlineData("gate")]
    [InlineData("package")]
    [InlineData("new-attempt")]
    public async Task Run_EvidenceChangesDuringSyntheticLogin_StopsBeforeTransport(string fault)
    {
        // Arrange
        using var fixture = new NuGetProvenanceTests.ProvenanceFixture();
        await fixture.InitializeAsync(TestContext.Current.CancellationToken);
        using var sink = new OfflineUploadSink(fixture)
        {
            OnLogin = () => Invalidate(fixture, fault),
        };

        // Act
        var result = await fixture.RunAsync(TestContext.Current.CancellationToken, sink);

        // Assert
        Assert.True(result.IsFailure, fault);
        Assert.Equal(1, sink.Logins);
        Assert.Equal(0, sink.Transports);
        Assert.Empty(sink.Requests);
    }

    [Theory]
    [InlineData("gate")]
    [InlineData("package")]
    [InlineData("new-attempt")]
    public async Task Run_EvidenceChangesAfterFirstRequest_StopsRemainingFiles(string fault)
    {
        // Arrange
        using var fixture = new NuGetProvenanceTests.ProvenanceFixture();
        await fixture.InitializeAsync(TestContext.Current.CancellationToken);
        using var sink = new OfflineUploadSink(fixture)
        {
            OnRequest = () => Invalidate(fixture, fault),
        };

        // Act
        var result = await fixture.RunAsync(TestContext.Current.CancellationToken, sink);

        // Assert
        Assert.True(result.IsFailure, fault);
        Assert.Equal(NuGetUploadOutcome.Accepted, Assert.Single(sink.Attempts).Outcome);
        Assert.Single(sink.Requests);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task Run_SecondResponseUncertain_PreservesPartialOutcomeAndStopsWithoutRetry(
        bool lostResponse
    )
    {
        // Arrange
        using var fixture = new NuGetProvenanceTests.ProvenanceFixture();
        await fixture.InitializeAsync(TestContext.Current.CancellationToken);
        using var sink = new OfflineUploadSink(fixture)
        {
            UncertainRequest = 2,
            LoseResponse = lostResponse,
        };

        // Act
        var result = await fixture.RunAsync(TestContext.Current.CancellationToken, sink);

        // Assert
        Assert.True(result.IsFailure);
        Assert.Equal(2, sink.Requests.Count);
        Assert.Equal(
            [NuGetUploadOutcome.Accepted, NuGetUploadOutcome.Unknown],
            sink.Attempts.Select(a => a.Outcome)
        );
        Assert.True(sink.Attempts[1].RequiresReconciliation);
        Assert.DoesNotContain(NuGetUploadAdapterTests.UploadFixture.Key, result.Error);
    }

    [Fact]
    public async Task Run_SymbolSelectionWithMissingPackage_RejectsWholeBatchBeforeLogin()
    {
        // Arrange
        using var fixture = new NuGetProvenanceTests.ProvenanceFixture();
        await fixture.InitializeAsync(TestContext.Current.CancellationToken);
        using var sink = new OfflineUploadSink(fixture);
        var package = Directory.GetFiles(
            Path.Combine(fixture.Handoff.Input.Directory, "packages"),
            "*.nupkg"
        )[0];
        File.Delete(package);

        // Act
        var result = await fixture.RunAsync(
            TestContext.Current.CancellationToken,
            sink,
            ["AndreGoepel.AppFoundation.2.2.0.snupkg"]
        );

        // Assert
        Assert.True(result.IsFailure);
        Assert.Equal(0, sink.Logins);
        Assert.Empty(sink.Requests);
    }

    private static void Invalidate(NuGetProvenanceTests.ProvenanceFixture fixture, string fault)
    {
        if (fault == "gate")
            fixture.Handoff.Evidence.Main.Responses["qualitygates/project_status"][
                "projectStatus"
            ]!["status"] = "ERROR";
        else if (fault == "package")
            fixture.Handoff.ChangeFiles("package");
        else
            fixture.Invalidate(fault);
    }

    // Test-only bridge: the production rehearsal always uses its fixed discard sink.
    private sealed class OfflineUploadSink(NuGetProvenanceTests.ProvenanceFixture fixture)
        : INuGetRehearsalSink,
            IDisposable
    {
        private NuGetUploadCredential? credential;
        internal int Logins { get; private set; }
        internal int Transports { get; private set; }
        internal int ChecksAtLogin { get; private set; }
        internal List<int> ChecksAtRequest { get; } = [];
        internal List<NuGetUploadAttempt> Attempts { get; } = [];
        internal List<(PackageEvidence Package, string Hash, string Endpoint)> Requests { get; } =
        [];
        internal Action? OnLogin { get; init; }
        internal Action? OnRequest { get; init; }
        internal int UncertainRequest { get; init; }
        internal bool LoseResponse { get; init; }

        public Task<Result> SimulateLoginAsync(CancellationToken ct)
        {
            ct.ThrowIfCancellationRequested();
            Logins++;
            ChecksAtLogin = fixture.Handoff.Checks;
            credential = new NuGetUploadCredential(
                NuGetUploadAdapterTests.UploadFixture.Key,
                fixture.Handoff.Clock.GetUtcNow().AddMinutes(30)
            );
            OnLogin?.Invoke();
            return Task.FromResult(Result.Ok());
        }

        public async Task<Result> SimulateUploadAsync(
            PackageEvidence package,
            Stream bytes,
            CancellationToken ct
        )
        {
            var adapter = new NuGetUploadAdapter(
                fixture.Handoff.Clock,
                () =>
                {
                    Transports++;
                    return new OfflineHandler(this, fixture, package);
                }
            );
            var result = await adapter.SendAsync(package, bytes, credential!, ct);
            if (result.IsFailure)
                return Result.Fail(result.Error);
            Attempts.Add(result.Value!);
            return result.Value!.RequiresReconciliation
                ? Result.Fail("Reconcile the uncertain upload before continuing.")
                : Result.Ok();
        }

        public void Dispose() => credential?.Dispose();

        private sealed class OfflineHandler(
            OfflineUploadSink sink,
            NuGetProvenanceTests.ProvenanceFixture fixture,
            PackageEvidence package
        ) : HttpMessageHandler
        {
            protected override async Task<HttpResponseMessage> SendAsync(
                HttpRequestMessage request,
                CancellationToken cancellationToken
            )
            {
                Assert.Equal(HttpMethod.Put, request.Method);
                Assert.Equal(
                    NuGetUploadAdapterTests.UploadFixture.Key,
                    request.Headers.GetValues("X-NuGet-ApiKey").Single()
                );
                var content = Assert.Single(
                    Assert.IsType<MultipartContent>(request.Content, exactMatch: false)
                );
                var bytes = await content.ReadAsByteArrayAsync(cancellationToken);
                sink.ChecksAtRequest.Add(fixture.Handoff.Checks);
                sink.Requests.Add(
                    (package, SourceFiles.Hash(bytes), request.RequestUri!.AbsoluteUri)
                );
                sink.OnRequest?.Invoke();
                if (sink.Requests.Count != sink.UncertainRequest)
                    return new HttpResponseMessage(HttpStatusCode.Created);
                if (sink.LoseResponse)
                    throw new HttpRequestException("Synthetic lost response");
                return new HttpResponseMessage(HttpStatusCode.Conflict);
            }
        }
    }
}
