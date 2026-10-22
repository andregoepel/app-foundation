using System.Text.Json;
using AndreGoepel.AppFoundation.Sonar;
using Xunit;

namespace AndreGoepel.AppFoundation.Sonar.Tests;

public sealed class AnalysisRunnerTests
{
    [Fact]
    public async Task Run_CompleteEvidence_VerifiesGateAndCleansPrivateScannerState()
    {
        // Arrange
        using var fixture = new RunnerFixture();

        // Act
        var result = await fixture.RunAsync(TestContext.Current.CancellationToken);

        // Assert
        Assert.True(result.IsSuccess, result.Error);

        using var document = JsonDocument.Parse(
            await File.ReadAllTextAsync(
                Path.Combine(fixture.Output, "publish/summary.json"),
                TestContext.Current.CancellationToken
            )
        );
        Assert.Equal("complete", document.RootElement.GetProperty("state").GetString());
        Assert.False(document.RootElement.GetProperty("releaseEligible").GetBoolean());
        Assert.Equal("analysis-id", document.RootElement.GetProperty("analysisId").GetString());
        Assert.DoesNotContain("secret", document.RootElement.GetRawText());
        Assert.False(document.RootElement.TryGetProperty("testFailure", out _));

        var beginLog = await File.ReadAllTextAsync(
            Path.Combine(fixture.Output, "private/scanner-begin.log"),
            TestContext.Current.CancellationToken
        );
        Assert.Equal("[REDACTED] [REDACTED]", beginLog);

        Assert.False(Directory.Exists(fixture.Scanner));
        Assert.False(File.Exists(Path.Combine(fixture.Root, ".sonar-analysis.lock")));

        Assert.True(
            fixture.Commands.FindIndex(c =>
                c.Contains("SonarQubeTargetsImported=true", StringComparison.Ordinal)
            )
                < fixture.Commands.FindIndex(c =>
                    c.Contains("sonarscanner begin", StringComparison.Ordinal)
                )
        );
    }

    [Theory]
    [InlineData("test-process-failure", "available", 1)]
    [InlineData("test-process-missing", "unavailable", 1)]
    [InlineData("test-process-invalid", "invalid", 1)]
    [InlineData("test-process-start", "unavailable", null)]
    [InlineData("tests", "available", 0)]
    public async Task Run_FailedTestProcess_RetainsSafeDiagnosticsWithoutSubmitting(
        string fault,
        string reportStatus,
        int? exitCode
    )
    {
        // Arrange
        using var fixture = new RunnerFixture { Fault = fault };

        // Act
        var result = await fixture.RunAsync(TestContext.Current.CancellationToken);

        // Assert
        Assert.True(result.IsFailure);
        var json = await File.ReadAllTextAsync(
            Path.Combine(fixture.Output, "publish/summary.json"),
            TestContext.Current.CancellationToken
        );
        using var document = JsonDocument.Parse(json);
        var diagnostic = document.RootElement.GetProperty("testFailure");
        Assert.Equal("failed", document.RootElement.GetProperty("state").GetString());
        Assert.False(document.RootElement.GetProperty("releaseEligible").GetBoolean());
        Assert.Equal(RunnerFixture.TestProject, diagnostic.GetProperty("project").GetString());
        Assert.Equal(reportStatus, diagnostic.GetProperty("reportStatus").GetString());
        Assert.Equal(exitCode, diagnostic.GetProperty("exitCode").Deserialize<int?>());
        Assert.DoesNotContain("private", json);
        Assert.DoesNotContain("secret", json);
        Assert.DoesNotContain(
            fixture.Commands,
            c => c.Contains("sonarscanner end", StringComparison.Ordinal)
        );
        Assert.Empty(document.RootElement.GetProperty("tests").EnumerateObject());
        Assert.False(Directory.Exists(fixture.Scanner));
        Assert.False(File.Exists(Path.Combine(fixture.Root, ".sonar-analysis.lock")));

        if (fault == "test-process-failure")
        {
            var failure = Assert.Single(diagnostic.GetProperty("failedTests").EnumerateArray());
            Assert.Equal("AndreGoepel.Fixture.Test", failure.GetProperty("method").GetString());
            Assert.Equal("timeout", failure.GetProperty("category").GetString());
        }
        if (fault == "tests")
            Assert.Equal(1, diagnostic.GetProperty("skipped").GetInt32());
    }

    [Theory]
    [InlineData("sdk")]
    [InlineData("format")]
    [InlineData("audit")]
    [InlineData("scope")]
    [InlineData("discovery")]
    [InlineData("tests")]
    [InlineData("coverage")]
    [InlineData("merge")]
    [InlineData("exclusions")]
    public async Task Run_InvalidPreUploadEvidence_FailsWithoutSubmitting(string fault)
    {
        // Arrange
        using var fixture = new RunnerFixture { Fault = fault };

        // Act
        var result = await fixture.RunAsync(TestContext.Current.CancellationToken);

        // Assert
        Assert.True(result.IsFailure);
        Assert.DoesNotContain(
            fixture.Commands,
            c => c.Contains("sonarscanner end", StringComparison.Ordinal)
        );
        Assert.False(Directory.Exists(fixture.Scanner));
        Assert.False(File.Exists(Path.Combine(fixture.Root, ".sonar-analysis.lock")));
    }

    [Theory]
    [InlineData("task")]
    [InlineData("latest")]
    [InlineData("gate")]
    [InlineData("empty-gate")]
    [InlineData("assignment")]
    [InlineData("metrics")]
    [InlineData("import")]
    [InlineData("scanner-exit")]
    public async Task Run_InvalidSonarReceipt_FailsDespiteSuccessfulTestExecution(string fault)
    {
        // Arrange
        using var fixture = new RunnerFixture { Fault = fault };

        // Act
        var result = await fixture.RunAsync(TestContext.Current.CancellationToken);

        // Assert
        Assert.True(result.IsFailure);

        using var document = JsonDocument.Parse(
            await File.ReadAllTextAsync(
                Path.Combine(fixture.Output, "publish/summary.json"),
                TestContext.Current.CancellationToken
            )
        );
        Assert.Equal("failed", document.RootElement.GetProperty("state").GetString());
        Assert.False(Directory.Exists(fixture.Scanner));
    }

    [Fact]
    public async Task Run_ChangedSource_FailsAndRemovesOwnedLock()
    {
        // Arrange
        using var fixture = new RunnerFixture { Fault = "source-change" };

        // Act
        var result = await fixture.RunAsync(TestContext.Current.CancellationToken);

        // Assert
        Assert.True(result.IsFailure);
        Assert.DoesNotContain(
            fixture.Commands,
            c => c.Contains("sonarscanner end", StringComparison.Ordinal)
        );
        Assert.False(File.Exists(Path.Combine(fixture.Root, ".sonar-analysis.lock")));
    }

    [Theory]
    [InlineData(".sonarqube/marker")]
    [InlineData("artifacts/sonar/test-run/publish/summary.json")]
    [InlineData(".sonar-analysis.lock")]
    public async Task Run_PreexistingState_PreservesOriginalEvidence(string path)
    {
        // Arrange
        using var fixture = new RunnerFixture();
        fixture.Write(path, "original");

        // Act
        var result = await fixture.RunAsync(TestContext.Current.CancellationToken);

        // Assert
        Assert.True(result.IsFailure);
        Assert.Equal(
            "original",
            await File.ReadAllTextAsync(
                Path.Combine(fixture.Root, path),
                TestContext.Current.CancellationToken
            )
        );
        Assert.Empty(fixture.Commands);
    }

    [Fact]
    public async Task Run_WrongServer_StopsBeforeLocalMutation()
    {
        // Arrange
        using var fixture = new RunnerFixture { Fault = "server" };

        // Act
        var result = await fixture.RunAsync(TestContext.Current.CancellationToken);

        // Assert
        Assert.True(result.IsFailure);
        Assert.False(Directory.Exists(fixture.Output));
    }
}
