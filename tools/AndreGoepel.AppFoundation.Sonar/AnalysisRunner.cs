using System.Globalization;
using System.Text.Json;
using System.Text.RegularExpressions;
using System.Xml.Linq;
using AndreGoepel.Core;

namespace AndreGoepel.AppFoundation.Sonar;

internal sealed partial class AnalysisRunner(IProcessRunner processes, ISonarClient sonar)
{
    internal async Task<Result> RunAsync(
        AnalysisOptions options,
        AnalysisContract contract,
        string scanToken,
        string verifyToken,
        CancellationToken cancellationToken
    )
    {
        var target = options.ValidateTarget();
        if (target.IsFailure)
            return target;

        if (string.IsNullOrEmpty(scanToken) || string.IsNullOrEmpty(verifyToken))
            return Result.Fail("Both local Sonar credentials are required.");

        var status = await sonar.GetAsync("system/status", null, cancellationToken);
        if (
            status.GetProperty("id").GetString() != options.ServerId
            || status.GetProperty("status").GetString() != "UP"
            || status.GetProperty("version").GetString() != contract.ServerVersion
        )
            return Result.Fail("Unexpected local server identity, version or status.");

        var lockPath = Path.Combine(options.Root, ".sonar-analysis.lock");
        FileStream runLock;

        try
        {
            runLock = new FileStream(
                lockPath,
                FileMode.CreateNew,
                FileAccess.Write,
                FileShare.None
            );
        }
        catch (IOException)
        {
            return Result.Fail("An analysis lock already exists; inspect it before retrying.");
        }

        await using (runLock)
        {
            var context = new RunContext(
                options,
                contract,
                scanToken,
                verifyToken,
                processes,
                sonar
            );

            Result outcome;
            Result cleanup;

            try
            {
                outcome = await context.ExecuteAsync(cancellationToken);
            }
            finally
            {
                try
                {
                    cleanup = await context.CleanupAsync();
                }
                finally
                {
                    await runLock.DisposeAsync();
                    File.Delete(lockPath);
                }
            }

            return cleanup.IsFailure
                ? Result.Fail($"{outcome.Error} {cleanup.Error}".Trim())
                : outcome;
        }
    }

    private sealed class RunContext(
        AnalysisOptions options,
        AnalysisContract contract,
        string scanToken,
        string verifyToken,
        IProcessRunner processes,
        ISonarClient sonar
    )
    {
        private readonly string output = Path.Combine(
            options.Root,
            "artifacts",
            "sonar",
            options.RunName
        );
        private readonly string scanner = Path.Combine(options.Root, ".sonarqube");
        private readonly AnalysisSummary summary = new()
        {
            Project = options.Project,
            ServerId = options.ServerId,
            Sdk = contract.Sdk,
        };

        private bool outputOwned;
        private bool scannerOwned;

        private string Private => Path.Combine(output, "private");
        private string Reports => Path.Combine(Private, "reports");

        internal async Task<Result> ExecuteAsync(CancellationToken ct)
        {
            if (Directory.Exists(scanner) || File.Exists(scanner))
                return Result.Fail("Stale .sonarqube path: inspect it before a new run.");

            if (Directory.Exists(output) || File.Exists(output))
                return Result.Fail("Run name already exists; previous evidence is preserved.");

            Directory.CreateDirectory(output);
            outputOwned = true;
            Directory.CreateDirectory(Private);
            Directory.CreateDirectory(Path.Combine(output, "publish"));
            Directory.CreateDirectory(Reports);
            if (!OperatingSystem.IsWindows())
                File.SetUnixFileMode(
                    output,
                    UnixFileMode.UserRead | UnixFileMode.UserWrite | UnixFileMode.UserExecute
                );

            summary.SourceSha256 = await SourceFiles.SnapshotAsync(options.Root, ct);
            summary.ContractSha256 = SourceFiles.Hash(
                await File.ReadAllBytesAsync(
                    Path.Combine(options.Root, "scripts/sonar/contract.json"),
                    ct
                )
            );

            var sdk = await CommandAsync("sdk", ["dotnet", "--version"], ct);
            if (sdk.IsFailure)
                return sdk;
            if (sdk.Value!.Text.Trim() != contract.Sdk)
                return Result.Fail("SDK version differs from the contract.");

            var solution = XDocument.Parse(
                await File.ReadAllTextAsync(Path.Combine(options.Root, contract.Solution), ct)
            );
            var projects = solution
                .Descendants("Project")
                .Select(p => p.Attribute("Path")!.Value)
                .ToArray();
            var tests = projects
                .Where(p => p.StartsWith("tests/", StringComparison.Ordinal))
                .Select(p => p.Split('/')[1])
                .ToHashSet(StringComparer.Ordinal);
            if (!tests.SetEquals(contract.Tests.Keys))
                return Result.Fail("Test project inventory drift.");

            var preparation = await PrepareAsync(projects.Length, ct);
            if (preparation.IsFailure)
                return preparation;

            var merged = Path.Combine(Reports, "coverage.xml");
            var begin = await BeginAsync(merged, ct);
            if (begin.IsFailure)
                return begin;

            var build = await CommandAsync(
                "analysis-build",
                [
                    "dotnet",
                    "build",
                    contract.Solution,
                    "-c",
                    "Release",
                    "--no-restore",
                    "--no-incremental",
                    "--disable-build-servers",
                ],
                ct
            );
            if (build.IsFailure)
                return build;

            var scope = await ReportValidation.ProjectScopeAsync(
                scanner,
                options.Root,
                projects,
                ct
            );
            if (scope.IsFailure)
                return scope;

            summary.AnalyzedProjects = scope.Value!;

            var coverage = await CollectCoverageAsync(merged, ct);
            if (coverage.IsFailure)
                return coverage;

            if (await SourceFiles.SnapshotAsync(options.Root, ct) != summary.SourceSha256)
                return Result.Fail("Sources changed during analysis; upload refused.");

            var end = await CommandAsync(
                "scanner-end",
                ["dotnet", "sonarscanner", "end", "/d:sonar.token=" + scanToken],
                ct,
                allowFailure: true
            );
            if (end.IsFailure)
                return end;

            return await VerifyAsync(end.Value!, ct);
        }

        private async Task<Result> PrepareAsync(int projectCount, CancellationToken ct)
        {
            (string Label, string[] Args)[] initial =
            [
                ("tool-restore", ["dotnet", "tool", "restore"]),
                ("format", ["dotnet", "csharpier", "check", "."]),
                ("restore", ["dotnet", "restore", contract.Solution, "--locked-mode"]),
            ];

            foreach (var (label, args) in initial)
            {
                var result = await CommandAsync(label, args, ct);
                if (result.IsFailure)
                    return result;
            }

            var audit = await CommandAsync(
                "audit",
                [
                    "dotnet",
                    "list",
                    contract.Solution,
                    "package",
                    "--vulnerable",
                    "--include-transitive",
                    "--format",
                    "json",
                    "--no-restore",
                ],
                ct
            );
            if (audit.IsFailure)
                return audit;

            using var document = JsonDocument.Parse(audit.Value!.Text);
            var validation = ReportValidation.Audit(document.RootElement, projectCount);
            if (validation.IsFailure)
                return validation;

            var build = await CommandAsync(
                "ordinary-build",
                [
                    "dotnet",
                    "build",
                    contract.Solution,
                    "-c",
                    "Release",
                    "--no-restore",
                    "--no-incremental",
                    "--disable-build-servers",
                    "-p:SonarQubeTargetsImported=true",
                ],
                ct
            );
            if (build.IsFailure)
                return build;

            return await CommandAsync(
                "playwright",
                [
                    "pwsh",
                    Path.Combine(
                        options.Root,
                        "tests/AndreGoepel.AppFoundation.E2ETests/bin/Release/net10.0/playwright.ps1"
                    ),
                    "install",
                    "chromium",
                ],
                ct
            );
        }

        private async Task<Result> BeginAsync(string merged, CancellationToken ct)
        {
            var settings = await sonar.GetAsync(
                "settings/values",
                scanToken,
                ct,
                ("component", options.Project),
                ("keys", "sonar.coverage.exclusions")
            );
            if (
                settings
                    .GetProperty("settings")
                    .EnumerateArray()
                    .Any(s =>
                        (
                            s.TryGetProperty("value", out var value)
                            && !string.IsNullOrEmpty(value.GetString())
                        )
                        || (
                            s.TryGetProperty("values", out var values)
                            && values.GetArrayLength() > 0
                        )
                    )
            )
                return Result.Fail("Server coverage exclusions require review.");

            scannerOwned = true;

            return await CommandAsync(
                "scanner-begin",
                [
                    "dotnet",
                    "sonarscanner",
                    "begin",
                    "/k:" + options.Project,
                    "/d:sonar.host.url=" + options.Url.AbsoluteUri,
                    "/d:sonar.token=" + scanToken,
                    "/d:sonar.scanner.scanAll=true",
                    "/d:sonar.scm.disabled=true",
                    "/d:sonar.qualitygate.wait=true",
                    "/d:sonar.qualitygate.timeout=300",
                    "/d:sonar.exclusions=" + string.Join(',', contract.SourceExclusions),
                    "/d:sonar.cs.vstest.reportsPaths=" + Path.Combine(Reports, "*", "tests.trx"),
                    "/d:sonar.cs.vscoveragexml.reportsPaths=" + merged,
                ],
                ct
            );
        }

        private async Task<Result> CollectCoverageAsync(string merged, CancellationToken ct)
        {
            var files = new List<string>();
            var checksums = new Dictionary<string, string>(StringComparer.Ordinal);
            var mapped = new HashSet<string>(StringComparer.Ordinal);
            var executed = new HashSet<string>(StringComparer.Ordinal);

            foreach (var (project, minimum) in contract.Tests)
            {
                string[] testArgs =
                [
                    "dotnet",
                    "test",
                    "--project",
                    $"tests/{project}/{project}.csproj",
                    "-c",
                    "Release",
                    "--no-build",
                    "--no-restore",
                    "--no-ansi",
                    "--no-progress",
                ];

                var discovery = await CommandAsync(
                    project + "-discovery",
                    [.. testArgs, "--list-tests"],
                    ct
                );
                if (discovery.IsFailure)
                    return discovery;

                var names = ReportValidation.Discovery(discovery.Value!.Text, minimum);
                if (names.IsFailure)
                    return names;

                var directory = Path.Combine(Reports, project);
                Directory.CreateDirectory(directory);
                var coveragePath = Path.Combine(directory, "coverage.xml");

                var run = await CommandAsync(
                    project,
                    [
                        "dotnet",
                        "coverage",
                        "collect",
                        "-f",
                        "xml",
                        "-o",
                        coveragePath,
                        "--",
                        .. testArgs,
                        "--timeout",
                        "10m",
                        "--minimum-expected-tests",
                        names.Value!.Length.ToString(CultureInfo.InvariantCulture),
                        "--report-xunit-xml",
                        "--report-xunit-xml-filename",
                        "tests.xml",
                        "--report-xunit-trx",
                        "--report-xunit-trx-filename",
                        "tests.trx",
                        "--results-directory",
                        directory,
                    ],
                    ct
                );
                if (run.IsFailure)
                    return run;

                var tests = ReportValidation.Tests(
                    await ReportValidation.ReadFreshAsync(
                        Path.Combine(directory, "tests.xml"),
                        summary.StartedAt,
                        ct
                    ),
                    names.Value
                );
                if (tests.IsFailure)
                    return tests;

                var anchors = project.EndsWith("E2ETests", StringComparison.Ordinal)
                    ? contract.CoverageAnchors
                    : [];
                var coverage = await ReadCoverageAsync(coveragePath, anchors, null, ct);
                if (coverage.IsFailure)
                    return coverage;

                var evidence = coverage.Value!;
                foreach (var pair in evidence.SourceChecksums)
                    checksums[pair.Key] = pair.Value;

                mapped.UnionWith(evidence.MappedSources);
                executed.UnionWith(evidence.ExecutedSources);
                summary.Tests[project] = new TestEvidence(
                    names.Value.Length,
                    tests.Value,
                    evidence
                );
                files.Add(coveragePath);
            }

            var merge = await CommandAsync(
                "coverage-merge",
                ["dotnet", "coverage", "merge", .. files, "-f", "xml", "-o", merged],
                ct
            );
            if (merge.IsFailure)
                return merge;

            var result = await ReadCoverageAsync(merged, contract.CoverageAnchors, checksums, ct);
            if (result.IsFailure)
                return result;
            if (
                !mapped.SetEquals(result.Value!.MappedSources)
                || !executed.SetEquals(result.Value.ExecutedSources)
            )
                return Result.Fail("Merged coverage differs from its verified inputs.");

            summary.Coverage = result.Value;
            return Result.Ok();
        }

        private async Task<Result<CoverageEvidence>> ReadCoverageAsync(
            string path,
            string[] anchors,
            IReadOnlyDictionary<string, string>? checksums,
            CancellationToken ct
        ) =>
            await ReportValidation.CoverageAsync(
                await ReportValidation.ReadFreshAsync(path, summary.StartedAt, ct),
                options.Root,
                anchors,
                checksums,
                ct
            );

        private async Task<Result> VerifyAsync(CommandOutput end, CancellationToken ct)
        {
            var receipt = (
                await File.ReadAllLinesAsync(
                    Path.Combine(scanner, "out/.sonar/report-task.txt"),
                    ct
                )
            )
                .Where(line => line.Contains('='))
                .Select(line => line.Split('=', 2))
                .ToDictionary(parts => parts[0], parts => parts[1], StringComparer.Ordinal);
            if (
                !receipt.TryGetValue("ceTaskId", out var taskId)
                || string.IsNullOrWhiteSpace(taskId)
                || receipt.GetValueOrDefault("projectKey") != options.Project
            )
                return Result.Fail("Invalid scanner task receipt.");

            var task = (await sonar.GetAsync("ce/task", scanToken, ct, ("id", taskId))).GetProperty(
                "task"
            );
            if (
                task.GetProperty("status").GetString() != "SUCCESS"
                || task.GetProperty("componentKey").GetString() != options.Project
            )
                return Result.Fail("Analysis task did not succeed.");

            var analysisId = task.GetProperty("analysisId").GetString()!;
            summary.TaskId = taskId;
            summary.AnalysisId = analysisId;
            summary.ScannerExitCode = end.ExitCode;

            var latest = (
                await sonar.GetAsync(
                    "project_analyses/search",
                    verifyToken,
                    ct,
                    ("project", options.Project),
                    ("ps", "1")
                )
            ).GetProperty("analyses");
            if (
                latest.GetArrayLength() == 0
                || latest[0].GetProperty("key").GetString() != analysisId
            )
                return Result.Fail("Another analysis replaced this project's latest result.");

            var gate = (
                await sonar.GetAsync(
                    "qualitygates/project_status",
                    verifyToken,
                    ct,
                    ("analysisId", analysisId)
                )
            ).GetProperty("projectStatus");
            var assignment = await sonar.GetAsync(
                "qualitygates/get_by_project",
                verifyToken,
                ct,
                ("project", options.Project)
            );
            if (
                gate.GetProperty("conditions").GetArrayLength() == 0
                || assignment.GetProperty("qualityGate").GetProperty("name").GetString()
                    != contract.Gate
            )
                return Result.Fail("Empty or unexpected quality gate.");

            var measures = (
                await sonar.GetAsync(
                    "measures/component",
                    verifyToken,
                    ct,
                    ("component", options.Project),
                    (
                        "metricKeys",
                        "coverage,lines_to_cover,uncovered_lines,ncloc,violations,security_hotspots,tests,test_success_density"
                    )
                )
            )
                .GetProperty("component")
                .GetProperty("measures");
            summary.Measures = measures
                .EnumerateArray()
                .ToDictionary(
                    m => m.GetProperty("metric").GetString()!,
                    m => m.GetProperty("value").GetString()!,
                    StringComparer.Ordinal
                );
            summary.Gate = gate;

            if (
                !summary.Measures.ContainsKey("coverage")
                || !double.TryParse(
                    summary.Measures.GetValueOrDefault("lines_to_cover"),
                    CultureInfo.InvariantCulture,
                    out var lines
                )
                || lines <= 0
                || !CoverageImport().IsMatch(end.Text)
            )
                return Result.Fail("Sonar did not confirm mapped coverage import.");

            if (await SourceFiles.SnapshotAsync(options.Root, ct) != summary.SourceSha256)
                return Result.Fail("Sources changed before verification completed.");

            if (end.ExitCode != 0 || gate.GetProperty("status").GetString() != "OK")
                return Result.Fail("Full analysis completed, but the quality gate failed.");

            summary.State = "complete";
            return Result.Ok();
        }

        private async Task<Result<CommandOutput>> CommandAsync(
            string label,
            string[] arguments,
            CancellationToken ct,
            bool allowFailure = false
        )
        {
            Console.WriteLine(label);
            var result = await processes.RunAsync(options.Root, arguments, ct);
            if (result.IsFailure)
                return Result.Fail<CommandOutput>(label + ": " + result.Error);

            var outputText = result
                .Value!.Text.Replace(scanToken, "[REDACTED]", StringComparison.Ordinal)
                .Replace(verifyToken, "[REDACTED]", StringComparison.Ordinal);
            await File.WriteAllTextAsync(Path.Combine(Private, label + ".log"), outputText, ct);

            return allowFailure || result.Value.ExitCode == 0
                ? Result.Ok(new CommandOutput(result.Value.ExitCode, outputText))
                : Result.Fail<CommandOutput>(label + " failed; see its private log.");
        }

        internal async Task<Result> CleanupAsync()
        {
            var cleanup = RemoveScannerDirectory();
            if (cleanup.IsFailure)
                summary.State = "failed";

            try
            {
                if (outputOwned)
                {
                    summary.FinishedAt = DateTimeOffset.UtcNow;
                    await File.WriteAllTextAsync(
                        Path.Combine(output, "publish/summary.json"),
                        JsonSerializer.Serialize(summary, JsonFormat.Options),
                        CancellationToken.None
                    );
                }
            }
            catch (Exception error) when (error is IOException or UnauthorizedAccessException)
            {
                return Result.Fail($"Could not write the analysis receipt. {cleanup.Error}".Trim());
            }

            return cleanup;
        }

        private Result RemoveScannerDirectory()
        {
            try
            {
                if (scannerOwned && Directory.Exists(scanner))
                {
                    if ((File.GetAttributes(scanner) & FileAttributes.ReparsePoint) != 0)
                        return Result.Fail("Unsafe scanner cleanup path; inspect it manually.");

                    Directory.Delete(scanner, recursive: true);
                }

                return Result.Ok();
            }
            catch (Exception error) when (error is IOException or UnauthorizedAccessException)
            {
                return Result.Fail(
                    "Scanner cleanup failed; inspect the private working directory."
                );
            }
        }
    }

    [GeneratedRegex("[1-9]\\d* main files with coverage", RegexOptions.CultureInvariant)]
    private static partial Regex CoverageImport();
}
