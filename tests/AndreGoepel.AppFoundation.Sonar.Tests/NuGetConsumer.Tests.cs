using System.Text.Json;
using AndreGoepel.AppFoundation.Sonar;
using Xunit;

namespace AndreGoepel.AppFoundation.Sonar.Tests;

public sealed class NuGetConsumerTests
{
    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task Run_AuthenticatedBatch_DiscardsVerifiedBytesAndReturnsDiagnosticReceipt(
        bool symbolsOnly
    )
    {
        // Arrange
        using var fixture = new NuGetProvenanceTests.ProvenanceFixture();
        await fixture.InitializeAsync(TestContext.Current.CancellationToken);
        var selected = symbolsOnly ? new[] { "AndreGoepel.AppFoundation.2.2.0.snupkg" } : null;
        var options = new NuGetRehearsalOptions(fixture.Handoff.Input, 500, 1, selected);

        // Act
        var result = await fixture
            .Consumer()
            .RunAsync(options, TestContext.Current.CancellationToken);

        // Assert
        Assert.True(result.IsSuccess, result.Error);
        Assert.Equal(symbolsOnly ? 1 : 12, result.Value!.Accepted.Length);
        Assert.Equal(symbolsOnly ? 6 : 28, fixture.Downloads);
        Assert.True(result.Value.DryRun);
        Assert.False(result.Value.ReleaseEligible);
        Assert.All(result.Value.Accepted, package => Assert.Equal(NuGetRelease.Feed, package.Feed));
        Assert.All(fixture.StorageAuthorization, Assert.Null);
        var json = JsonSerializer.Serialize(result.Value, JsonFormat.Options);
        Assert.Contains("\"releaseEligible\": false", json);
    }

    [Theory]
    [InlineData("policy-id")]
    [InlineData("wrong-workflow")]
    [InlineData("new-attempt")]
    [InlineData("expired-run")]
    [InlineData("missing-artifact")]
    public async Task Run_UntrustedProducer_ReturnsNoReceipt(string fault)
    {
        // Arrange
        using var fixture = new NuGetProvenanceTests.ProvenanceFixture();
        await fixture.InitializeAsync(TestContext.Current.CancellationToken);
        fixture.Invalidate(fault);

        // Act
        var result = await fixture
            .Consumer()
            .RunAsync(
                new NuGetRehearsalOptions(fixture.Handoff.Input, 500, 1, null),
                TestContext.Current.CancellationToken
            );

        // Assert
        Assert.True(result.IsFailure, fault);
        Assert.Equal(0, fixture.Downloads);
    }

    [Theory]
    [InlineData("gate")]
    [InlineData("package")]
    [InlineData("attempt-pin")]
    [InlineData("run-pin")]
    [InlineData("unknown-file")]
    public async Task Run_InvalidHandoffOrSelection_ReturnsNoReceipt(string fault)
    {
        // Arrange
        using var fixture = new NuGetProvenanceTests.ProvenanceFixture();
        await fixture.InitializeAsync(TestContext.Current.CancellationToken);
        if (fault == "gate")
            fixture.Handoff.Evidence.Main.Responses["qualitygates/project_status"][
                "projectStatus"
            ]!["status"] = "ERROR";
        if (fault == "package")
            fixture.Handoff.ChangeFiles("package");
        var options = new NuGetRehearsalOptions(
            fixture.Handoff.Input,
            fault == "run-pin" ? 999 : 500,
            fault == "attempt-pin" ? 2 : 1,
            fault == "unknown-file" ? ["other.nupkg"] : null
        );

        // Act
        var result = await fixture
            .Consumer()
            .RunAsync(options, TestContext.Current.CancellationToken);

        // Assert
        Assert.True(result.IsFailure, fault);
    }

    [Fact]
    public async Task Run_ProvenanceChangesDuringConsumption_ReturnsNoSuccessReceipt()
    {
        // Arrange
        using var fixture = new NuGetProvenanceTests.ProvenanceFixture();
        await fixture.InitializeAsync(TestContext.Current.CancellationToken);
        fixture.OnDownload = () =>
        {
            if (fixture.Downloads == 7)
                fixture.Invalidate("newer-run");
        };

        // Act
        var result = await fixture
            .Consumer()
            .RunAsync(
                new NuGetRehearsalOptions(fixture.Handoff.Input, 500, 1, null),
                TestContext.Current.CancellationToken
            );

        // Assert
        Assert.True(result.IsFailure);
        Assert.Equal(7, fixture.Downloads);
    }

    [Theory]
    [InlineData("before-login")]
    [InlineData("wrong-feed")]
    [InlineData("changed-bytes")]
    [InlineData("valid")]
    public async Task Sink_ConsumesOnlyApprovedSnapshot_ValidatesSimulatedLoginAndHash(
        string scenario
    )
    {
        // Arrange
        var sink = new NuGetDiscardSink();
        var token = TestContext.Current.CancellationToken;
        if (scenario != "before-login")
            await sink.SimulateLoginAsync(token);
        var bytes = "synthetic-package"u8.ToArray();
        var package = new PackageEvidence(
            "test.nupkg",
            "test",
            "2.2.0",
            SourceFiles.Hash(bytes),
            scenario == "wrong-feed" ? "https://other.example" : NuGetRelease.Feed
        );
        using var stream = new MemoryStream(
            scenario == "changed-bytes" ? [0] : bytes,
            writable: false
        );

        // Act
        var result = await sink.SimulateUploadAsync(package, stream, token);

        // Assert
        Assert.Equal(scenario == "valid", result.IsSuccess);
        Assert.Equal(scenario is "valid" or "changed-bytes" ? stream.Length : 0, stream.Position);
    }

    [Fact]
    public async Task Sink_Cancellation_StopsLoginAndConsumption()
    {
        // Arrange
        var sink = new NuGetDiscardSink();
        using var cancellation = new CancellationTokenSource();
        await cancellation.CancelAsync();
        using var stream = new MemoryStream([1]);
        var package = new PackageEvidence("test.nupkg", "test", "2.2.0", "", NuGetRelease.Feed);

        // Act
        var login = await Record.ExceptionAsync(() => sink.SimulateLoginAsync(cancellation.Token));
        var upload = await Record.ExceptionAsync(() =>
            sink.SimulateUploadAsync(package, stream, cancellation.Token)
        );

        // Assert
        Assert.IsType<OperationCanceledException>(login);
        Assert.IsType<OperationCanceledException>(upload);
        Assert.Equal(0, stream.Position);
    }
}
