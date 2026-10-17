using System.Text.Json;
using System.Xml.Linq;
using AndreGoepel.Core;

namespace AndreGoepel.AppFoundation.Sonar;

internal static class ReadinessValidation
{
    internal static async Task<Result<ReadinessReceipt>> ValidateAsync(
        NuGetRelease release,
        MainPolicy policy,
        AnalysisContract contract,
        byte[] receiptBytes,
        byte[] summaryBytes,
        JsonElement producer,
        DateTimeOffset now,
        CancellationToken ct
    )
    {
        using var document = JsonDocument.Parse(receiptBytes);
        using var analysisDocument = JsonDocument.Parse(summaryBytes);
        var json = document.RootElement;
        var summary = analysisDocument.RootElement;
        var receipt = json.Deserialize<ReadinessReceipt>(JsonFormat.Options)!;
        if (
            json.GetProperty("schemaVersion").GetInt32() != 1
            || !json.GetProperty("releaseEligible").GetBoolean()
            || summary.GetProperty("schemaVersion").GetInt32() != 2
            || summary.GetProperty("releaseEligible").GetBoolean()
            || receipt.Repository != MainPolicy.Repository
            || receipt.Run.Sha != release.Sha
            || receipt.Run.RunId != release.ReadinessRunId
            || receipt.Run.Attempt != producer.GetProperty("run_attempt").GetInt32()
            || receipt.WorkflowPath != MainPolicy.WorkflowPath
            || receipt.WorkflowRevision != release.Sha
            || receipt.PolicyVersion != policy.Version
            || receipt.PolicySha256 != policy.Fingerprint()
            || receipt.BaselineAnalysisId != policy.BaselineAnalysisId
            || receipt.AnalysisReceiptSha256 != SourceFiles.Hash(summaryBytes)
            || receipt.AnalysisContractSha256
                != SourceFiles.Hash(
                    await File.ReadAllBytesAsync(
                        Path.Combine(release.Root, "scripts/sonar/contract.json"),
                        ct
                    )
                )
            || receipt.SourceSha256 != await SourceFiles.SnapshotAsync(release.Root, ct)
        )
            return Result.Fail<ReadinessReceipt>(
                "Readiness schema, identity, policy or source hashes did not verify."
            );

        var started = producer.GetProperty("run_started_at").GetDateTimeOffset();
        if (
            receipt.IssuedAt < started
            || receipt.IssuedAt > now
            || receipt.ExpiresAt <= now
            || receipt.ExpiresAt <= receipt.IssuedAt
            || receipt.ExpiresAt - receipt.IssuedAt
                > TimeSpan.FromMinutes(policy.EvidenceLifetimeMinutes)
            || receipt.MandatoryWorkflows.Length != policy.Workflows.Length
            || receipt.MandatoryWorkflows.Any(w =>
                w.StartedAt > receipt.IssuedAt
                || receipt.ExpiresAt > w.StartedAt.AddMinutes(policy.MaximumRunAgeMinutes)
            )
        )
            return Result.Fail<ReadinessReceipt>(
                "Readiness expired or has an invalid lifetime; trusted revalidation is required."
            );

        if (
            summary.GetProperty("state").GetString() != "complete"
            || summary.GetProperty("project").GetString() != MainPolicy.Project
            || summary.GetProperty("serverId").GetString() != MainPolicy.ServerId
            || summary.GetProperty("sdk").GetString() != contract.Sdk
            || summary.GetProperty("scannerExitCode").GetInt32() != 0
            || summary.GetProperty("sourceSha256").GetString() != receipt.SourceSha256
            || summary.GetProperty("contractSha256").GetString() != receipt.AnalysisContractSha256
            || summary.GetProperty("analysisId").GetString() != receipt.AnalysisId
            || summary.GetProperty("taskId").GetString() != receipt.TaskId
            || string.IsNullOrWhiteSpace(receipt.AnalysisId)
            || string.IsNullOrWhiteSpace(receipt.TaskId)
            || summary.GetProperty("startedAt").GetDateTimeOffset() < started
            || summary.GetProperty("finishedAt").GetDateTimeOffset() > receipt.IssuedAt
            || summary.GetProperty("finishedAt").GetDateTimeOffset()
                < summary.GetProperty("startedAt").GetDateTimeOffset()
        )
            return Result.Fail<ReadinessReceipt>(
                "The complete analysis receipt does not bind to readiness."
            );

        await using var solutionStream = File.OpenRead(
            Path.Combine(release.Root, contract.Solution)
        );
        var solution = await XDocument.LoadAsync(solutionStream, LoadOptions.None, ct);
        var expectedProjects = solution
            .Descendants("Project")
            .Select(p => (string)p.Attribute("Path")!)
            .Order(StringComparer.Ordinal);
        if (
            !summary
                .GetProperty("analyzedProjects")
                .EnumerateArray()
                .Select(p => p.GetString())
                .Order(StringComparer.Ordinal)
                .SequenceEqual(expectedProjects)
        )
            return Result.Fail<ReadinessReceipt>(
                "Analysis scope does not cover the complete solution."
            );

        var tests = summary.GetProperty("tests");
        if (
            tests.EnumerateObject().Count() != contract.Tests.Count
            || contract.Tests.Any(expected =>
                !tests.TryGetProperty(expected.Key, out var actual)
                || actual.GetProperty("discovered").GetInt32() < expected.Value
                || actual.GetProperty("passed").GetInt32()
                    != actual.GetProperty("discovered").GetInt32()
            )
        )
            return Result.Fail<ReadinessReceipt>(
                "Analysis is missing complete mandatory test evidence."
            );

        var measures = summary
            .GetProperty("measures")
            .Deserialize<Dictionary<string, string>>(JsonFormat.Options)!;
        var gate = MainSonarPolicy.EvaluateGate(
            summary.GetProperty("gate"),
            policy.Conditions,
            measures
        );
        return gate.IsSuccess ? Result.Ok(receipt) : Result.Fail<ReadinessReceipt>(gate.Error);
    }
}
