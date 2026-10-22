using System.Text.Json;
using System.Xml.Linq;
using AndreGoepel.AppFoundation.Sonar;
using Xunit;

namespace AndreGoepel.AppFoundation.Sonar.Tests;

public sealed class TestFailureDiagnosticsTests
{
    private const string Method = "AndreGoepel.Fixture.Tests.Example_Fails";
    private static readonly DateTimeOffset Started = DateTimeOffset.UtcNow.AddMinutes(-1);

    [Fact]
    public async Task Read_PrivateFailureData_PublishesOnlyDiscoveredMethodAndCategory()
    {
        // Arrange
        using var fixture = new RunnerFixture();
        var name = Method + "(password: \"private-password\")";
        var report = RunnerFixture.Tests(ReportValidation.EscapeTestName(name), "Fail");
        report
            .Descendants("test")
            .Single()
            .Add(
                Failure("System.TimeoutException"),
                new XElement("output", "private browser trace and credentials")
            );
        report.Save(Path.Combine(fixture.Root, "tests.xml"));

        // Act
        var evidence = await ReadAsync(fixture, [name]);

        // Assert
        Assert.Equal("available", evidence.ReportStatus);
        Assert.Equal(1, evidence.Executed);
        Assert.Equal(1, evidence.Failed);
        Assert.Equal(0, evidence.Skipped);
        Assert.Equal(0, evidence.Errors);
        var failure = Assert.Single(evidence.FailedTests!);
        Assert.Equal(Method, failure.Method);
        Assert.Equal("timeout", failure.Category);
        var json = JsonSerializer.Serialize(evidence);
        Assert.DoesNotContain("private", json);
        Assert.DoesNotContain("password", json);
        Assert.DoesNotContain("System.TimeoutException", json);
    }

    [Theory]
    [InlineData("System.OperationCanceledException", "cancelled")]
    [InlineData("System.Threading.Tasks.TaskCanceledException", "cancelled")]
    [InlineData("Microsoft.Playwright.PlaywrightException", "browser")]
    [InlineData("Xunit.Sdk.EqualException", "test-framework")]
    [InlineData("private-exception-data", "other")]
    public async Task Read_AssemblyFailure_RetainsOnlyAllowlistedCategory(
        string exception,
        string category
    )
    {
        // Arrange
        using var fixture = new RunnerFixture();
        var report = RunnerFixture.Tests(Method);
        report
            .Root!.Element("assembly")!
            .Add(
                new XElement(
                    "errors",
                    new XElement(
                        "error",
                        new XAttribute("name", "private-error"),
                        Failure(exception)
                    )
                )
            );
        report.Save(Path.Combine(fixture.Root, "tests.xml"));

        // Act
        var evidence = await ReadAsync(fixture, [Method]);

        // Assert
        Assert.Equal(1, evidence.Errors);
        Assert.Equal(category, Assert.Single(evidence.ErrorCategories!));
        Assert.Empty(evidence.FailedTests!);
        Assert.DoesNotContain("private", JsonSerializer.Serialize(evidence));
    }

    [Theory]
    [InlineData("missing", "unavailable")]
    [InlineData("empty", "unavailable")]
    [InlineData("stale", "unavailable")]
    [InlineData("malformed", "invalid")]
    [InlineData("wrong-root", "invalid")]
    [InlineData("dtd", "invalid")]
    [InlineData("oversized", "invalid")]
    public async Task Read_UnusableReport_DoesNotMaskOriginalProcessFailure(
        string fault,
        string status
    )
    {
        // Arrange
        using var fixture = new RunnerFixture();
        var path = Path.Combine(fixture.Root, "tests.xml");
        if (fault != "missing")
        {
            var xml = fault switch
            {
                "empty" => "",
                "malformed" => "<private-unclosed",
                "wrong-root" => "<private-root />",
                "dtd" =>
                    "<!DOCTYPE assemblies [<!ENTITY secret SYSTEM 'file:///private'>]><assemblies>&secret;</assemblies>",
                "oversized" => new string('x', 8 * 1024 * 1024 + 1),
                _ => RunnerFixture.Tests(Method).ToString(),
            };
            await File.WriteAllTextAsync(path, xml, TestContext.Current.CancellationToken);
            if (fault == "stale")
                File.SetLastWriteTimeUtc(path, Started.AddMinutes(-1).UtcDateTime);
        }

        // Act
        var evidence = await ReadAsync(fixture, [Method]);

        // Assert
        Assert.Equal(status, evidence.ReportStatus);
        Assert.Equal(1, evidence.ExitCode);
        Assert.Null(evidence.Executed);
        Assert.Null(evidence.FailedTests);
        Assert.DoesNotContain("private", JsonSerializer.Serialize(evidence));
    }

    [Theory]
    [InlineData("private-unlisted-name", false)]
    [InlineData("AndreGoepel.Fixture.Tests.Unknown", false)]
    [InlineData("AndreGoepel.Fixture.Tests.<private>", true)]
    public async Task Read_UnrecognizedOrUnsafeName_WithholdsName(string name, bool discovered)
    {
        // Arrange
        using var fixture = new RunnerFixture();
        RunnerFixture.Tests(name, "Fail").Save(Path.Combine(fixture.Root, "tests.xml"));

        // Act
        var evidence = await ReadAsync(fixture, discovered ? [name] : [Method]);

        // Assert
        Assert.Null(Assert.Single(evidence.FailedTests!).Method);
        Assert.DoesNotContain(name, JsonSerializer.Serialize(evidence));
    }

    [Fact]
    public async Task Read_ManyFailures_BoundsDetailsAndPreservesTotal()
    {
        // Arrange
        using var fixture = new RunnerFixture();
        var report = RunnerFixture.Tests(Method, "Fail");
        var collection = report.Descendants("collection").Single();
        var test = collection.Element("test")!;
        for (var index = 0; index < TestFailureDiagnostics.MaximumFailedTests; index++)
            collection.Add(new XElement(test));
        report.Save(Path.Combine(fixture.Root, "tests.xml"));

        // Act
        var evidence = await ReadAsync(fixture, [Method]);

        // Assert
        Assert.Equal(TestFailureDiagnostics.MaximumFailedTests + 1, evidence.Failed);
        Assert.Equal(TestFailureDiagnostics.MaximumFailedTests, evidence.FailedTests!.Length);
    }

    private static XElement Failure(string exception) =>
        new(
            "failure",
            new XAttribute("exception-type", exception),
            new XElement("message", "private user data"),
            new XElement("stack-trace", "private host path")
        );

    private static Task<TestFailureEvidence> ReadAsync(
        RunnerFixture fixture,
        string[] discovered
    ) =>
        TestFailureDiagnostics.ReadAsync(
            RunnerFixture.TestProject,
            1,
            Path.Combine(fixture.Root, "tests.xml"),
            Started,
            discovered,
            TestContext.Current.CancellationToken
        );
}
