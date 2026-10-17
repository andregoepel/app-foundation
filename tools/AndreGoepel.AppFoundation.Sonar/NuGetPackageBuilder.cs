using System.Globalization;
using System.Text.Json;
using System.Xml.Linq;
using AndreGoepel.Core;

namespace AndreGoepel.AppFoundation.Sonar;

internal interface INuGetPackageBuilder
{
    Task<Result<PackageEvidence[]>> BuildAsync(
        NuGetRelease release,
        string output,
        CancellationToken ct
    );
}

internal sealed class NuGetPackageBuilder(IProcessRunner processes, AnalysisContract contract)
    : INuGetPackageBuilder
{
    internal async Task<Result<CommandOutput>> CommandAsync(
        string root,
        string[] args,
        CancellationToken ct
    )
    {
        var output = await processes.RunAsync(root, args, ct);
        return output.IsFailure || output.Value!.ExitCode != 0
            ? Result.Fail<CommandOutput>(
                "Release preparation command failed; no publication is permitted."
            )
            : output;
    }

    public async Task<Result<PackageEvidence[]>> BuildAsync(
        NuGetRelease release,
        string output,
        CancellationToken ct
    )
    {
        var solution = XDocument.Load(Path.Combine(release.Root, contract.Solution));
        var projects = solution
            .Descendants("Project")
            .Select(p => (string)p.Attribute("Path")!)
            .ToArray();
        var clean = await CommandAsync(
            release.Root,
            [
                "dotnet",
                "clean",
                contract.Solution,
                "-c",
                "Release",
                "-p:SonarQubeTargetsImported=true",
            ],
            ct
        );
        if (clean.IsFailure)
            return Result.Fail<PackageEvidence[]>(clean.Error);

        var restore = await CommandAsync(
            release.Root,
            ["dotnet", "restore", contract.Solution, "--locked-mode"],
            ct
        );
        if (restore.IsFailure)
            return Result.Fail<PackageEvidence[]>(restore.Error);

        var before = await CompilationAsync(release.Root, projects, [], ct);
        if (before.IsFailure)
            return Result.Fail<PackageEvidence[]>(before.Error);

        string[] version =
        [
            $"-p:Version={release.Version}",
            $"-p:RepositoryCommit={release.Sha}",
            $"-p:SourceRevisionId={release.Sha}",
            "-p:ContinuousIntegrationBuild=true",
            "-p:SonarQubeTargetsImported=true",
        ];
        restore = await CommandAsync(
            release.Root,
            ["dotnet", "restore", contract.Solution, "--locked-mode", .. version],
            ct
        );
        if (restore.IsFailure)
            return Result.Fail<PackageEvidence[]>(restore.Error);

        var after = await CompilationAsync(release.Root, projects, version, ct);
        if (after.IsFailure || !before.Value!.SequenceEqual(after.Value!))
            return Result.Fail<PackageEvidence[]>(
                "Versioned compilation inputs differ from main; review and analyze the difference before release."
            );

        var build = await CommandAsync(
            release.Root,
            [
                "dotnet",
                "build",
                contract.Solution,
                "-c",
                "Release",
                "--no-restore",
                "--no-incremental",
                "--disable-build-servers",
                .. version,
            ],
            ct
        );
        if (build.IsFailure)
            return Result.Fail<PackageEvidence[]>(build.Error);

        var audit = await CommandAsync(
            release.Root,
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
            return Result.Fail<PackageEvidence[]>(audit.Error);
        using var auditDocument = JsonDocument.Parse(audit.Value!.Text);
        var checkedAudit = ReportValidation.Audit(auditDocument.RootElement, projects.Length);
        if (checkedAudit.IsFailure)
            return Result.Fail<PackageEvidence[]>(checkedAudit.Error);

        var tests = await TestAsync(release, output, version, ct);
        if (tests.IsFailure)
            return Result.Fail<PackageEvidence[]>(tests.Error);

        var packages = Path.Combine(output, "packages");
        Directory.CreateDirectory(packages);
        foreach (var id in NuGetRelease.Packages)
        {
            var pack = await CommandAsync(
                release.Root,
                [
                    "dotnet",
                    "pack",
                    $"src/{id}/{id}.csproj",
                    "-c",
                    "Release",
                    "--no-build",
                    "--no-restore",
                    "-o",
                    packages,
                    .. version,
                ],
                ct
            );
            if (pack.IsFailure)
                return Result.Fail<PackageEvidence[]>(pack.Error);
        }

        return await NuGetPackages.InspectAsync(release, packages, ct);
    }

    private async Task<Result<string[]>> CompilationAsync(
        string root,
        string[] projects,
        string[] version,
        CancellationToken ct
    )
    {
        var inputs = new List<string>();
        foreach (var project in projects)
        {
            var result = await CommandAsync(
                root,
                [
                    "dotnet",
                    "msbuild",
                    project,
                    "-nologo",
                    "-p:Configuration=Release",
                    "-getProperty:TargetFramework,TargetFrameworks,DefineConstants,LangVersion,Nullable,AllowUnsafeBlocks,Optimize,CheckForOverflowUnderflow",
                    "-getItem:Compile,ProjectReference,PackageReference,Analyzer,AdditionalFiles,EmbeddedResource,Content,RazorComponent,RazorGenerate",
                    .. version,
                ],
                ct
            );
            if (result.IsFailure)
                return Result.Fail<string[]>(result.Error);

            using var document = JsonDocument.Parse(result.Value!.Text);
            inputs.Add(JsonSerializer.Serialize(document.RootElement));
        }

        return Result.Ok(inputs.ToArray());
    }

    private async Task<Result> TestAsync(
        NuGetRelease release,
        string output,
        string[] version,
        CancellationToken ct
    )
    {
        foreach (var (project, minimum) in contract.Tests)
        {
            string[] args =
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
                .. version,
            ];
            var discovery = await CommandAsync(release.Root, [.. args, "--list-tests"], ct);
            if (discovery.IsFailure)
                return discovery;
            var names = ReportValidation.Discovery(discovery.Value!.Text, minimum);
            if (names.IsFailure)
                return names;

            var directory = Path.Combine(output, "tests", project);
            Directory.CreateDirectory(directory);
            var started = DateTimeOffset.UtcNow;
            var run = await CommandAsync(
                release.Root,
                [
                    .. args,
                    "--timeout",
                    "10m",
                    "--minimum-expected-tests",
                    names.Value!.Length.ToString(CultureInfo.InvariantCulture),
                    "--report-xunit-xml",
                    "--report-xunit-xml-filename",
                    "tests.xml",
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
                    started,
                    ct
                ),
                names.Value!
            );
            if (tests.IsFailure)
                return tests;
        }

        return Result.Ok();
    }
}
