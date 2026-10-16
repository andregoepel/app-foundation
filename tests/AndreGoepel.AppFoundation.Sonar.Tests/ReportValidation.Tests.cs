using System.Text.Json;
using System.Xml.Linq;
using AndreGoepel.AppFoundation.Sonar;
using Xunit;

namespace AndreGoepel.AppFoundation.Sonar.Tests;

public sealed class ReportValidationTests
{
    [Fact]
    public void Tests_LiteralBackslashesAndControlCharacters_MatchExactReporterEscaping()
    {
        // Arrange
        const string name =
            "Test(\"{\\\"path\\\":\\\"C:\\\\temp\\\"}\", \\n, \n\r\t\0\a\b\f\v\u0001\uffff, 😀)";
        const string escaped =
            "Test(\\\"{\\\\\\\"path\\\\\\\":\\\\\\\"C:\\\\\\\\temp\\\\\\\"}\\\", \\\\n, \\n\\r\\t\\0\\a\\b\\f\\v\\x01\\xffff, 😀)";

        // Act
        var result = ReportValidation.Tests(RunnerFixture.Tests(escaped), [name]);

        // Assert
        Assert.True(result.IsSuccess, result.Error);
    }

    [Theory]
    [InlineData("https://sonar.andregoepel.dev", "app-foundation-local", "local-id")]
    [InlineData("http://localhost:9000/path", "app-foundation-local", "local-id")]
    [InlineData("http://user@localhost:9000", "app-foundation-local", "local-id")]
    [InlineData("http://localhost:9000?q=1", "app-foundation-local", "local-id")]
    [InlineData("http://localhost:9000/#fragment", "app-foundation-local", "local-id")]
    [InlineData("http://example.com:9000", "app-foundation-local", "local-id")]
    [InlineData("https://localhost:9000", "app-foundation-local", "local-id")]
    [InlineData("http://localhost:9000", "app-foundation", "local-id")]
    [InlineData("http://localhost:9000", "app-foundation-local", "AA2DD71C-AaFDycIuQLOXJXX6YWNZ")]
    public void Parse_UnsafeTarget_RejectsBeforeExecution(string url, string project, string id)
    {
        // Act
        var result = AnalysisOptions.Parse([
            "--root",
            Path.GetTempPath(),
            "--run-name",
            "local-01",
            "--url",
            url,
            "--project",
            project,
            "--server-id",
            id,
        ]);

        // Assert
        Assert.True(result.IsFailure);
    }

    [Theory]
    [InlineData("http://127.0.0.1:9000")]
    [InlineData("http://localhost:9000/")]
    [InlineData("http://[::1]:9000")]
    public void Parse_LoopbackTarget_Accepts(string url)
    {
        // Act
        var result = AnalysisOptions.Parse([
            "--root",
            Path.GetTempPath(),
            "--run-name",
            "local-01",
            "--url",
            url,
            "--server-id",
            "local-id",
        ]);

        // Assert
        Assert.True(result.IsSuccess, result.Error);
    }

    [Theory]
    [InlineData("--run-name")]
    [InlineData("--unknown", "value")]
    [InlineData("--root", "relative")]
    [InlineData("--run-name", "../escape")]
    [InlineData("--run-name", "one", "--run-name", "two")]
    public void Parse_InvalidArguments_Rejects(params string[] args)
    {
        // Act
        var result = AnalysisOptions.Parse(args);

        // Assert
        Assert.True(result.IsFailure);
    }

    [Theory]
    [InlineData("Discovered 0 tests.\n")]
    [InlineData("  AndreGoepel.Test\nDiscovered 2 tests.\n")]
    [InlineData("  AndreGoepel.Test\n  AndreGoepel.Test\nDiscovered 2 tests.\n")]
    public void Discovery_IncompleteOrDuplicateInventory_Rejects(string output)
    {
        // Act
        var result = ReportValidation.Discovery(output, 1);

        // Assert
        Assert.True(result.IsFailure);
    }

    [Fact]
    public void Tests_EscapedTheoryName_MatchesDiscovery()
    {
        // Arrange
        var document = RunnerFixture.Tests("Test(value: \\\"example\\\")");

        // Act
        var result = ReportValidation.Tests(document, ["Test(value: \"example\")"]);

        // Assert
        Assert.True(result.IsSuccess, result.Error);
        Assert.Equal(1, result.Value);
    }

    [Theory]
    [InlineData("other", "Pass")]
    [InlineData("expected", "Skip")]
    [InlineData("expected", "Fail")]
    public void Tests_MismatchedOrUnsuccessfulExecution_Rejects(string name, string outcome)
    {
        // Act
        var result = ReportValidation.Tests(RunnerFixture.Tests(name, outcome), ["expected"]);

        // Assert
        Assert.True(result.IsFailure);
    }

    [Theory]
    [InlineData("missing")]
    [InlineData("empty")]
    [InlineData("stale")]
    public async Task ReadFresh_MissingEmptyOrStaleReport_Rejects(string kind)
    {
        // Arrange
        using var fixture = new RunnerFixture();
        var path = Path.Combine(fixture.Root, "report.xml");
        if (kind != "missing")
            await File.WriteAllTextAsync(
                path,
                kind == "empty" ? "" : "<results />",
                TestContext.Current.CancellationToken
            );

        if (kind == "stale")
            File.SetLastWriteTimeUtc(path, DateTime.UtcNow.AddDays(-1));

        // Act / Assert
        await Assert.ThrowsAsync<InvalidDataException>(() =>
            ReportValidation.ReadFreshAsync(
                path,
                DateTimeOffset.UtcNow.AddMinutes(-1),
                TestContext.Current.CancellationToken
            )
        );
    }

    [Fact]
    public async Task Coverage_WrongCheckout_Rejects()
    {
        // Arrange
        using var fixture = new RunnerFixture();
        var document = fixture.Coverage();
        document
            .Descendants("source_file")
            .Single()
            .SetAttributeValue("path", Path.Combine(Path.GetTempPath(), "other.cs"));

        // Act / Assert
        await Assert.ThrowsAsync<InvalidDataException>(() =>
            ReportValidation.CoverageAsync(
                document,
                fixture.Root,
                [],
                null,
                TestContext.Current.CancellationToken
            )
        );
    }

    [Theory]
    [InlineData("SHA256", "incorrect")]
    [InlineData("SHA1", "incorrect")]
    [InlineData("MD5", "incorrect")]
    [InlineData("", "")]
    public async Task Coverage_UnverifiedChecksum_Rejects(string algorithm, string checksum)
    {
        // Arrange
        using var fixture = new RunnerFixture();
        var document = fixture.Coverage();
        var source = document.Descendants("source_file").Single();
        source.SetAttributeValue("checksum_type", algorithm);
        source.SetAttributeValue("checksum", checksum);

        // Act
        var result = await ReportValidation.CoverageAsync(
            document,
            fixture.Root,
            [],
            null,
            TestContext.Current.CancellationToken
        );

        // Assert
        Assert.True(result.IsFailure);
    }

    [Fact]
    public async Task Coverage_MergedReport_RequiresPreviouslyVerifiedSourceChecksums()
    {
        // Arrange
        using var fixture = new RunnerFixture();
        var verified = await ReportValidation.CoverageAsync(
            fixture.Coverage(),
            fixture.Root,
            [RunnerFixture.Source],
            null,
            TestContext.Current.CancellationToken
        );

        // Act
        var merged = await ReportValidation.CoverageAsync(
            fixture.Coverage(checksum: false),
            fixture.Root,
            [RunnerFixture.Source],
            verified.Value!.SourceChecksums,
            TestContext.Current.CancellationToken
        );

        // Assert
        Assert.True(merged.IsSuccess, merged.Error);
        Assert.Equal([RunnerFixture.Source], merged.Value!.ExecutedSources);
    }

    [Fact]
    public async Task Coverage_MissingChildProcessAnchor_Rejects()
    {
        // Arrange
        using var fixture = new RunnerFixture();

        // Act
        var result = await ReportValidation.CoverageAsync(
            fixture.Coverage(),
            fixture.Root,
            ["samples/AppHost.cs"],
            null,
            TestContext.Current.CancellationToken
        );

        // Assert
        Assert.True(result.IsFailure);
    }

    [Fact]
    public async Task Coverage_EmptyReport_Rejects()
    {
        // Arrange
        using var fixture = new RunnerFixture();

        // Act
        var result = await ReportValidation.CoverageAsync(
            new XDocument(new XElement("results")),
            fixture.Root,
            [],
            null,
            TestContext.Current.CancellationToken
        );

        // Assert
        Assert.True(result.IsFailure);
    }

    [Theory]
    [InlineData("{}")]
    [InlineData("{\"projects\":[]}")]
    [InlineData("{\"projects\":[{}],\"problems\":[\"unavailable\"]}")]
    [InlineData(
        "{\"projects\":[{\"frameworks\":[{\"topLevelPackages\":[{\"vulnerabilities\":[\"CVE\"]}]}]}]}"
    )]
    public void Audit_IncompleteOrVulnerableDependencies_Rejects(string json)
    {
        // Arrange
        using var document = JsonDocument.Parse(json);

        // Act
        var result = ReportValidation.Audit(document.RootElement, 1);

        // Assert
        Assert.True(result.IsFailure);
    }

    [Fact]
    public async Task Snapshot_AuthoredChanges_ChangeDigestWhileArtifactsDoNot()
    {
        // Arrange
        using var fixture = new RunnerFixture();
        var before = await SourceFiles.SnapshotAsync(
            fixture.Root,
            TestContext.Current.CancellationToken
        );

        // Act
        fixture.Write("artifacts/generated.txt", "generated");
        var generated = await SourceFiles.SnapshotAsync(
            fixture.Root,
            TestContext.Current.CancellationToken
        );

        fixture.Write(RunnerFixture.Source, "changed");
        var changed = await SourceFiles.SnapshotAsync(
            fixture.Root,
            TestContext.Current.CancellationToken
        );

        // Assert
        Assert.Equal(before, generated);
        Assert.NotEqual(before, changed);
    }
}
