using System.Text.Json;
using System.Text.Json.Serialization;

namespace AndreGoepel.AppFoundation.Sonar;

internal sealed record AnalysisContract(
    string Sdk,
    string ServerVersion,
    string Gate,
    string Solution,
    Dictionary<string, int> Tests,
    string[] CoverageAnchors,
    string[] SourceExclusions
);

internal sealed record CoverageEvidence(
    string[] MappedSources,
    string[] ExecutedSources,
    [property: JsonIgnore] Dictionary<string, string> SourceChecksums
);

internal sealed record TestEvidence(int Discovered, int Passed, CoverageEvidence Coverage);

internal sealed class AnalysisSummary
{
    public int SchemaVersion { get; } = 2;
    public bool ReleaseEligible { get; } = false;
    public string State { get; set; } = "failed";

    public string Platform { get; } = Environment.OSVersion.Platform.ToString();
    public required string Project { get; init; }
    public required string ServerId { get; init; }
    public required string Sdk { get; init; }

    public DateTimeOffset StartedAt { get; init; } = DateTimeOffset.UtcNow;
    public DateTimeOffset FinishedAt { get; set; }

    public string? SourceSha256 { get; set; }
    public string? ContractSha256 { get; set; }

    public string[] AnalyzedProjects { get; set; } = [];
    public Dictionary<string, TestEvidence> Tests { get; } = new(StringComparer.Ordinal);

    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public TestFailureEvidence? TestFailure { get; set; }

    public CoverageEvidence? Coverage { get; set; }

    public string? TaskId { get; set; }
    public string? AnalysisId { get; set; }
    public int? ScannerExitCode { get; set; }

    public JsonElement? Gate { get; set; }
    public Dictionary<string, string> Measures { get; set; } = new(StringComparer.Ordinal);
}

internal static class JsonFormat
{
    internal static readonly JsonSerializerOptions Options = new(JsonSerializerDefaults.Web)
    {
        WriteIndented = true,
    };
}
