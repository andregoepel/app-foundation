using System.Text.Json;
using AndreGoepel.AppFoundation.Sonar;
using Xunit;

namespace AndreGoepel.AppFoundation.Sonar.Tests;

public sealed class NuGetVerificationTests
{
    [Theory]
    [InlineData(true, false)]
    [InlineData(true, true)]
    [InlineData(false, false)]
    [InlineData(false, true)]
    public async Task RunHandoff_ModeAndVerificationResult_EmitsOnlySuccessfulDiagnosticReceipt(
        bool verifyOnly,
        bool invalid
    )
    {
        // Arrange
        using var fixture = new NuGetProvenanceTests.ProvenanceFixture();
        await fixture.InitializeAsync(TestContext.Current.CancellationToken);
        if (invalid)
            fixture.Handoff.ChangeFiles("symbol");
        var options = Options(fixture) with { VerifyOnly = verifyOnly };
        using var output = new StringWriter();

        // Act
        var result = await NuGetCommand.RunHandoffAsync(
            fixture.Consumer(),
            options,
            output,
            TestContext.Current.CancellationToken
        );

        // Assert
        Assert.Equal(!invalid, result.IsSuccess);
        if (invalid)
        {
            Assert.Equal("", output.ToString());
            return;
        }
        using var document = JsonDocument.Parse(output.ToString());
        var receipt = document.RootElement;
        Assert.True(receipt.GetProperty("dryRun").GetBoolean());
        Assert.False(receipt.GetProperty("releaseEligible").GetBoolean());
        Assert.Equal(
            12,
            receipt.GetProperty(verifyOnly ? "packages" : "accepted").GetArrayLength()
        );
        Assert.Equal(verifyOnly ? 2 : 28, fixture.Downloads);
    }

    [Theory]
    [InlineData("v2.2.0")]
    [InlineData("sonar-poc/nuget/0123456789abcdef0123456789abcdef/v2.2.0")]
    public async Task Verify_AuthenticatedBatch_ReturnsAllFilesWithoutRehearsal(string tag)
    {
        // Arrange
        using var fixture = new NuGetProvenanceTests.ProvenanceFixture(tag: tag);
        await fixture.InitializeAsync(TestContext.Current.CancellationToken);

        // Act
        var result = await fixture
            .Consumer()
            .VerifyAsync(Options(fixture), TestContext.Current.CancellationToken);

        // Assert
        Assert.True(result.IsSuccess, result.Error);
        Assert.Equal(12, result.Value!.Packages.Length);
        Assert.Equal(2, fixture.Downloads);
        Assert.Equal(2, fixture.Handoff.Checks);
        Assert.True(result.Value.DryRun);
        Assert.False(result.Value.ReleaseEligible);
        Assert.All(result.Value.Packages, package => Assert.Equal(NuGetRelease.Feed, package.Feed));
        Assert.All(fixture.StorageAuthorization, Assert.Null);
        var json = JsonSerializer.Serialize(result.Value, JsonFormat.Options);
        Assert.Contains("\"packages\":", json);
        Assert.Contains("\"releaseEligible\": false", json);
        Assert.DoesNotContain("\"accepted\":", json);
    }

    [Theory]
    [InlineData("policy-id")]
    [InlineData("wrong-workflow")]
    [InlineData("workflow-content")]
    [InlineData("new-attempt")]
    [InlineData("expired-run")]
    [InlineData("missing-artifact")]
    [InlineData("artifact-sha")]
    public async Task Verify_UntrustedProducer_ReturnsNoInventory(string fault)
    {
        // Arrange
        using var fixture = new NuGetProvenanceTests.ProvenanceFixture();
        await fixture.InitializeAsync(TestContext.Current.CancellationToken);
        fixture.Invalidate(fault);

        // Act
        var result = await fixture
            .Consumer()
            .VerifyAsync(Options(fixture), TestContext.Current.CancellationToken);

        // Assert
        Assert.True(result.IsFailure, fault);
        Assert.Null(result.Value);
        Assert.Equal(0, fixture.Downloads);
        Assert.Equal(0, fixture.Handoff.Checks);
    }

    [Theory]
    [InlineData("gate")]
    [InlineData("main-moved")]
    [InlineData("tag")]
    [InlineData("package")]
    [InlineData("symbol")]
    [InlineData("missing-file")]
    [InlineData("extra-file")]
    public async Task Verify_EvidenceChangesAfterSuccess_RechecksWithoutReusingInventory(
        string fault
    )
    {
        // Arrange
        using var fixture = new NuGetProvenanceTests.ProvenanceFixture();
        await fixture.InitializeAsync(TestContext.Current.CancellationToken);
        var consumer = fixture.Consumer();
        var options = Options(fixture);
        var first = await consumer.VerifyAsync(options, TestContext.Current.CancellationToken);
        Assert.True(first.IsSuccess, first.Error);
        switch (fault)
        {
            case "gate":
                fixture.Handoff.Evidence.Main.Responses["qualitygates/project_status"][
                    "projectStatus"
                ]!["status"] = "ERROR";
                break;
            case "main-moved":
                fixture.Handoff.Evidence.Main.Responses["git/ref/heads/main"]["object"]!["sha"] =
                    new string('f', 40);
                break;
            case "tag":
                fixture.Handoff.Retarget();
                break;
            default:
                fixture.Handoff.ChangeFiles(fault);
                break;
        }

        // Act
        var result = await consumer.VerifyAsync(options, TestContext.Current.CancellationToken);

        // Assert
        Assert.True(result.IsFailure, fault);
        Assert.Null(result.Value);
        Assert.True(fixture.Downloads > 2);
    }

    [Fact]
    public async Task Verify_ExpiredEvidence_DoesNotRefreshEarlierSuccess()
    {
        // Arrange
        using var fixture = new NuGetProvenanceTests.ProvenanceFixture();
        await fixture.InitializeAsync(TestContext.Current.CancellationToken);
        var consumer = fixture.Consumer();
        var options = Options(fixture);
        var first = await consumer.VerifyAsync(options, TestContext.Current.CancellationToken);
        Assert.True(first.IsSuccess, first.Error);
        fixture.Handoff.Clock.Now = fixture.Handoff.Evidence.Main.Now.AddMinutes(31);

        // Act
        var result = await consumer.VerifyAsync(options, TestContext.Current.CancellationToken);

        // Assert
        Assert.True(result.IsFailure);
        Assert.Null(result.Value);
    }

    [Fact]
    public async Task Verify_ProvenanceChangesDuringReadiness_ReturnsNoInventory()
    {
        // Arrange
        using var fixture = new NuGetProvenanceTests.ProvenanceFixture();
        await fixture.InitializeAsync(TestContext.Current.CancellationToken);
        fixture.Handoff.OnVerify = () => fixture.Invalidate("new-attempt");

        // Act
        var result = await fixture
            .Consumer()
            .VerifyAsync(Options(fixture), TestContext.Current.CancellationToken);

        // Assert
        Assert.True(result.IsFailure);
        Assert.Null(result.Value);
        Assert.Equal(2, fixture.Handoff.Checks);
    }

    [Fact]
    public async Task Verify_Cancellation_StopsWithoutInventory()
    {
        // Arrange
        using var fixture = new NuGetProvenanceTests.ProvenanceFixture();
        await fixture.InitializeAsync(TestContext.Current.CancellationToken);
        using var cancellation = new CancellationTokenSource();
        await cancellation.CancelAsync();

        // Act
        var error = await Record.ExceptionAsync(() =>
            fixture.Consumer().VerifyAsync(Options(fixture), cancellation.Token)
        );

        // Assert
        Assert.IsType<OperationCanceledException>(error, exactMatch: false);
        Assert.Equal(0, fixture.Handoff.Checks);
    }

    private static NuGetHandoffOptions Options(NuGetProvenanceTests.ProvenanceFixture fixture) =>
        new(fixture.Handoff.Input, 500, 1, null, VerifyOnly: true);
}
