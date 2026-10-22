using System.Text.RegularExpressions;
using System.Xml;
using System.Xml.Linq;

namespace AndreGoepel.AppFoundation.Sonar;

internal sealed record FailedTestDiagnostic(string? Method, string Category);

internal sealed record TestFailureEvidence(
    string Project,
    int? ExitCode,
    string ReportStatus,
    int? Executed = null,
    int? Failed = null,
    int? Skipped = null,
    int? Errors = null,
    FailedTestDiagnostic[]? FailedTests = null,
    string[]? ErrorCategories = null
);

internal static partial class TestFailureDiagnostics
{
    private const int MaximumReportBytes = 8 * 1024 * 1024;
    internal const int MaximumFailedTests = 50;

    internal static async Task<TestFailureEvidence> ReadAsync(
        string project,
        int? exitCode,
        string path,
        DateTimeOffset startedAt,
        string[] discovered,
        CancellationToken cancellationToken
    )
    {
        var evidence = new TestFailureEvidence(project, exitCode, "unavailable");

        try
        {
            var file = new FileInfo(path);
            if (!file.Exists || file.Length == 0 || file.LastWriteTimeUtc < startedAt.UtcDateTime)
                return evidence;
            if (file.Length > MaximumReportBytes)
                return evidence with { ReportStatus = "invalid" };

            await using var stream = File.OpenRead(path);
            using var reader = XmlReader.Create(
                stream,
                new XmlReaderSettings
                {
                    Async = true,
                    DtdProcessing = DtdProcessing.Prohibit,
                    XmlResolver = null,
                    MaxCharactersInDocument = MaximumReportBytes,
                }
            );
            var document = await XDocument.LoadAsync(reader, LoadOptions.None, cancellationToken);
            if (
                document.Root?.Name != "assemblies"
                || document.Root.Elements("assembly").Count() != 1
            )
                return evidence with { ReportStatus = "invalid" };

            return Describe(evidence, document, discovered);
        }
        catch (Exception error) when (error is IOException or UnauthorizedAccessException)
        {
            return evidence;
        }
        catch (XmlException)
        {
            return evidence with { ReportStatus = "invalid" };
        }
    }

    private static TestFailureEvidence Describe(
        TestFailureEvidence evidence,
        XDocument document,
        string[] discovered
    )
    {
        var methods = new Dictionary<string, string>(StringComparer.Ordinal);
        foreach (var name in discovered)
        {
            var match = TestMethod().Match(name);
            if (match.Success && match.Groups[1].Length <= 512)
                methods[ReportValidation.EscapeTestName(name)] = match.Groups[1].Value;
        }

        var tests = document.Descendants("test").ToArray();
        var failed = tests.Where(t => (string?)t.Attribute("result") == "Fail").ToArray();
        var errors = document.Descendants("errors").Elements("error").ToArray();

        return evidence with
        {
            ReportStatus = "available",
            Executed = tests.Length,
            Failed = failed.Length,
            Skipped = tests.Count(t => (string?)t.Attribute("result") == "Skip"),
            Errors = errors.Length,
            FailedTests = failed
                .Take(MaximumFailedTests)
                .Select(t => new FailedTestDiagnostic(
                    methods.GetValueOrDefault((string?)t.Attribute("name") ?? ""),
                    Category(t.Element("failure"))
                ))
                .ToArray(),
            ErrorCategories = errors
                .Select(e => Category(e.Element("failure")))
                .Distinct(StringComparer.Ordinal)
                .Order(StringComparer.Ordinal)
                .ToArray(),
        };
    }

    private static string Category(XElement? failure) =>
        (string?)failure?.Attribute("exception-type") switch
        {
            "System.TimeoutException" => "timeout",
            "System.OperationCanceledException" or "System.Threading.Tasks.TaskCanceledException" =>
                "cancelled",
            "Microsoft.Playwright.PlaywrightException" => "browser",
            string type when type.StartsWith("Xunit.Sdk.", StringComparison.Ordinal) =>
                "test-framework",
            _ => "other",
        };

    [GeneratedRegex(
        @"\A(AndreGoepel(?:\.[A-Za-z_][A-Za-z0-9_]*)+)(?:\(|\z)",
        RegexOptions.CultureInvariant
    )]
    private static partial Regex TestMethod();
}
