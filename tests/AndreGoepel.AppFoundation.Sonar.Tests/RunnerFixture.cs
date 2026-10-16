using System.Text.Json;
using System.Xml.Linq;
using AndreGoepel.AppFoundation.Sonar;
using AndreGoepel.Core;

namespace AndreGoepel.AppFoundation.Sonar.Tests;

internal sealed class RunnerFixture : IProcessRunner, ISonarClient, IDisposable
{
    private static readonly string[] Vulnerabilities = ["CVE"];

    internal const string ProjectName = "AndreGoepel.Fixture";
    internal const string TestProject = "AndreGoepel.Fixture.E2ETests";
    internal const string Source = "tools/AndreGoepel.Fixture/Example.cs";

    internal string Root { get; } =
        Path.Combine(Path.GetTempPath(), "sonar-tests-" + Guid.NewGuid().ToString("N"));
    internal AnalysisOptions Options { get; }
    internal AnalysisContract Contract { get; }

    internal string Fault { get; set; } = "";
    internal List<string> Commands { get; } = [];

    internal string Output => Path.Combine(Root, "artifacts/sonar/test-run");
    internal string Scanner => Path.Combine(Root, ".sonarqube");
    internal string[] Projects { get; } =
    [$"tools/{ProjectName}/{ProjectName}.csproj", $"tests/{TestProject}/{TestProject}.csproj"];

    internal RunnerFixture()
    {
        Directory.CreateDirectory(Root);

        Options = new AnalysisOptions(
            Root,
            new Uri("http://127.0.0.1:9000"),
            "app-foundation-local",
            "local-id",
            "test-run"
        );
        Contract = new AnalysisContract(
            "10.0.402",
            "local-version",
            "reviewed-gate",
            "Example.slnx",
            new Dictionary<string, int> { [TestProject] = 1 },
            [Source],
            ["artifacts/**"]
        );

        foreach (var project in Projects)
            Write(project, "<Project />");

        Write(Source, "class Example {}\n");
        Write(
            Contract.Solution,
            new XDocument(
                new XElement(
                    "Solution",
                    Projects.Select(p => new XElement("Project", new XAttribute("Path", p)))
                )
            ).ToString()
        );
        Write(
            "scripts/sonar/contract.json",
            JsonSerializer.Serialize(Contract, JsonFormat.Options)
        );
    }

    internal void Write(string relative, string content)
    {
        var path = Path.Combine(Root, relative);
        Directory.CreateDirectory(Path.GetDirectoryName(path)!);
        File.WriteAllText(path, content);
    }

    internal XDocument Coverage(bool checksum = true, bool covered = true)
    {
        var source = new XElement(
            "source_file",
            new XAttribute("id", "1"),
            new XAttribute("path", Path.Combine(Root, Source))
        );
        if (checksum)
        {
            source.SetAttributeValue("checksum_type", "SHA256");
            source.SetAttributeValue(
                "checksum",
                SourceFiles.Hash(File.ReadAllBytes(Path.Combine(Root, Source)))
            );
        }

        var range = new XElement(
            "range",
            new XAttribute("source_id", "1"),
            new XAttribute("covered", covered ? "yes" : "no")
        );
        var functions = new XElement(
            "functions",
            new XElement("function", new XElement("ranges", range))
        );
        var module = new XElement(
            "module",
            new XAttribute("name", checksum ? ProjectName + ".dll" : ProjectName),
            new XElement("source_files", source),
            functions
        );

        return new XDocument(new XElement("results", new XElement("modules", module)));
    }

    internal static XDocument Tests(
        string name = "AndreGoepel.Fixture.Test",
        string outcome = "Pass"
    )
    {
        var test = new XElement(
            "test",
            new XAttribute("name", name),
            new XAttribute("result", outcome)
        );
        var assembly = new XElement(
            "assembly",
            new XAttribute("errors", "0"),
            new XAttribute("total", "1"),
            new XElement("collection", test)
        );

        return new XDocument(new XElement("assemblies", assembly));
    }

    internal Task<Result> RunAsync(CancellationToken ct) =>
        new AnalysisRunner(this, this).RunAsync(
            Options,
            Contract,
            "scan-secret",
            "verify-secret",
            ct
        );

    public Task<Result<CommandOutput>> RunAsync(
        string root,
        string[] arguments,
        CancellationToken cancellationToken
    )
    {
        var command = string.Join(' ', arguments);
        Commands.Add(command);

        var text = "";
        var exit = 0;
        if (command.Contains("--version", StringComparison.Ordinal))
            text = Fault == "sdk" ? "wrong" : Contract.Sdk;
        else if (command.Contains("csharpier", StringComparison.Ordinal) && Fault == "format")
            exit = 1;
        else if (command.Contains("--vulnerable", StringComparison.Ordinal))
            text = JsonSerializer.Serialize(
                new
                {
                    projects = Projects.Select(_ => new
                    {
                        frameworks = new[]
                        {
                            new
                            {
                                transitivePackages = Fault == "audit"
                                    ? new[] { new { vulnerabilities = Vulnerabilities } }
                                    : [],
                            },
                        },
                    }),
                }
            );
        else if (arguments.Contains("begin"))
        {
            Directory.CreateDirectory(Scanner);
            text = "scan-secret verify-secret";
        }
        else if (arguments.Contains("build") && Directory.Exists(Scanner))
        {
            for (var i = 0; i < Projects.Length; i++)
                Write(
                    $".sonarqube/out/{i}/ProjectInfo.xml",
                    $"<Project><FullPath>{Path.Combine(Root, Projects[i])}</FullPath><IsExcluded>{(Fault == "scope" ? "true" : "false")}</IsExcluded></Project>"
                );
        }
        else if (arguments.Contains("--list-tests"))
            text =
                Fault == "discovery"
                    ? "Discovered 0 tests.\n"
                    : "  AndreGoepel.Fixture.Test\nDiscovered 1 tests.\n";
        else if (arguments.Contains("collect"))
        {
            var directory = arguments[Array.IndexOf(arguments, "--results-directory") + 1];
            Tests(outcome: Fault == "tests" ? "Skip" : "Pass")
                .Save(Path.Combine(directory, "tests.xml"));
            Coverage(covered: Fault != "coverage").Save(Path.Combine(directory, "coverage.xml"));
            if (Fault == "source-change")
                Write(Source, "class Changed {}\n");
        }
        else if (arguments.Contains("merge"))
        {
            Coverage(checksum: false, covered: Fault != "merge")
                .Save(arguments[Array.IndexOf(arguments, "-o") + 1]);
        }
        else if (arguments.Contains("end"))
        {
            Write(
                ".sonarqube/out/.sonar/report-task.txt",
                "projectKey=app-foundation-local\nceTaskId=task-id\n"
            );
            text = Fault == "import" ? "No coverage imported" : "1 main files with coverage";
            if (Fault == "scanner-exit")
                exit = 1;
        }

        return Task.FromResult(Result.Ok(new CommandOutput(exit, text)));
    }

    public Task<JsonElement> GetAsync(
        string endpoint,
        string? token,
        CancellationToken cancellationToken,
        params (string Key, string Value)[] query
    )
    {
        object response = endpoint switch
        {
            "system/status" => new
            {
                id = Fault == "server" ? "other" : Options.ServerId,
                status = "UP",
                version = Contract.ServerVersion,
            },
            "settings/values" => new
            {
                settings = Fault == "exclusions" ? new[] { new { value = "**/*" } } : [],
            },
            "ce/task" => new
            {
                task = new
                {
                    status = Fault == "task" ? "FAILED" : "SUCCESS",
                    componentKey = Options.Project,
                    analysisId = "analysis-id",
                },
            },
            "project_analyses/search" => new
            {
                analyses = new[] { new { key = Fault == "latest" ? "other" : "analysis-id" } },
            },
            "qualitygates/project_status" => new
            {
                projectStatus = new
                {
                    status = Fault == "gate" ? "ERROR" : "OK",
                    conditions = Fault == "empty-gate" ? [] : new[] { new { status = "OK" } },
                },
            },
            "qualitygates/get_by_project" => new
            {
                qualityGate = new { name = Fault == "assignment" ? "other" : Contract.Gate },
            },
            "measures/component" => new
            {
                component = new
                {
                    measures = new[]
                    {
                        new { metric = "coverage", value = "90" },
                        new { metric = "lines_to_cover", value = Fault == "metrics" ? "0" : "10" },
                    },
                },
            },
            _ => throw new InvalidOperationException("Unexpected fixture endpoint"),
        };

        return Task.FromResult(JsonSerializer.SerializeToElement(response));
    }

    public void Dispose() => Directory.Delete(Root, recursive: true);
}
