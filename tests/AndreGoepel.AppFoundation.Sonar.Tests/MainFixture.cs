using System.Text.Json;
using System.Text.Json.Nodes;
using AndreGoepel.AppFoundation.Sonar;

namespace AndreGoepel.AppFoundation.Sonar.Tests;

internal sealed class MainFixture : IGitHubClient, ISonarClient
{
    internal const string Sha = "1234567890123456789012345678901234567890";
    internal const string Baseline = "12345678-1234-1234-1234-123456789012";
    internal const string GateName = "AppFoundation PoC 1.0.0";

    internal MainRunIdentity Identity { get; set; } = new(Sha, 100, 1, 200, 1);
    internal MainPolicy Policy { get; set; }
    internal Dictionary<string, JsonNode> Responses { get; } = new(StringComparer.Ordinal);
    internal List<string> Requests { get; } = [];
    internal DateTimeOffset Now { get; } = DateTimeOffset.UtcNow;
    internal Exception? Failure { get; set; }
    internal int Delays { get; private set; }
    internal Action? OnDelay { get; set; }

    internal MainFixture()
    {
        Policy = new MainPolicy(
            "1.0.0",
            Baseline,
            120,
            60,
            [
                new("violations", "GT", "0"),
                new("coverage", "LT", "80"),
                new("new_violations", "GT", "0"),
                new("new_coverage", "LT", "80"),
                new("new_duplicated_lines_density", "GT", "3"),
                new("new_security_hotspots_reviewed", "LT", "100"),
            ],
            [new("cs", GateName, 121, Now.AddDays(-1))],
            [
                new(
                    10,
                    ".github/workflows/ci.yml",
                    "hash",
                    "shared/ci@main",
                    Sha,
                    new()
                    {
                        ["ci / build-and-test"] = ["Build", "Test", "Vulnerability scan"],
                        ["servicedefaults-drift"] = ["Compare"],
                    }
                ),
                new(
                    20,
                    ".github/workflows/e2e.yml",
                    "hash",
                    "shared/e2e@main",
                    Sha,
                    new() { ["e2e / end-to-end"] = ["Run E2E tests"] }
                ),
            ]
        );

        Responses["git/ref/heads/main"] = Node(new { @object = new { sha = Sha } });
        Responses["actions/runs/100"] = Run(
            100,
            30,
            MainPolicy.WorkflowPath,
            "workflow_run",
            "in_progress"
        );
        foreach (var workflow in Policy.Workflows)
        {
            var id = workflow.Id == 10 ? 200 : 300;
            Responses[
                $"actions/workflows/{workflow.Id}/runs?branch=main&event=push&head_sha={Sha}&per_page=100"
            ] = Node(new { total_count = 1, workflow_runs = new[] { new { id } } });
            Responses[$"actions/runs/{id}"] = Run(
                id,
                workflow.Id,
                workflow.Path,
                "push",
                "completed",
                workflow
            );
            Responses[$"actions/runs/{id}/attempts/1/jobs?per_page=100"] = Node(
                new
                {
                    total_count = workflow.Jobs.Count,
                    jobs = workflow.Jobs.Select(
                        (entry, index) =>
                            new
                            {
                                id = id + index,
                                started_at = Now.AddMinutes(-4),
                                name = entry.Key,
                                status = "completed",
                                conclusion = "success",
                                steps = entry.Value.Select(name => new
                                {
                                    name,
                                    status = "completed",
                                    conclusion = "success",
                                }),
                            }
                    ),
                }
            );
        }

        Responses["qualitygates/show"] = Node(new { conditions = Policy.Conditions });
        Responses["qualityprofiles/search"] = Node(new { profiles = Policy.Profiles });
        Responses["settings/values"] = Node(
            new
            {
                settings = new[]
                {
                    new { key = "sonar.qualitygate.ignoreSmallChanges", value = "false" },
                },
            }
        );
        Responses["new_code_periods/show"] = Node(
            new { type = "SPECIFIC_ANALYSIS", value = Baseline }
        );
        Responses["measures/component"] = Node(
            new
            {
                component = new
                {
                    measures = new[]
                    {
                        new { metric = "new_lines", period = new { value = "0" } },
                        new { metric = "new_lines_to_cover", period = new { value = "0" } },
                        new { metric = "new_conditions_to_cover", period = new { value = "0" } },
                        new { metric = "new_security_hotspots", period = new { value = "0" } },
                    },
                },
            }
        );
    }

    internal JsonNode Run(
        long id,
        long workflowId,
        string path,
        string eventName,
        string status,
        WorkflowPolicy? workflow = null
    ) =>
        Node(
            new
            {
                id,
                workflow_id = workflowId,
                path,
                @event = eventName,
                status,
                conclusion = "success",
                head_sha = Sha,
                head_branch = "main",
                run_attempt = 1,
                created_at = Now.AddMinutes(-5),
                run_started_at = Now.AddMinutes(-5),
                repository = new { full_name = MainPolicy.Repository },
                head_repository = new { full_name = MainPolicy.Repository },
                referenced_workflows = new[]
                {
                    new { path = workflow?.SharedPath, sha = workflow?.SharedSha },
                },
            }
        );

    internal static Dictionary<string, string> Environment(string root) =>
        new(StringComparer.Ordinal)
        {
            ["GITHUB_ACTIONS"] = "true",
            ["GITHUB_REPOSITORY"] = MainPolicy.Repository,
            ["GITHUB_EVENT_NAME"] = "workflow_run",
            ["GITHUB_REF"] = "refs/heads/main",
            ["GITHUB_WORKFLOW_REF"] =
                $"{MainPolicy.Repository}/{MainPolicy.WorkflowPath}@refs/heads/main",
            ["GITHUB_WORKFLOW_SHA"] = Sha,
            ["GITHUB_SHA"] = Sha,
            ["GITHUB_WORKSPACE"] = root,
            ["GITHUB_RUN_ID"] = "100",
            ["GITHUB_RUN_ATTEMPT"] = "1",
        };

    internal JsonElement Payload() =>
        Element(new JsonObject { ["workflow_run"] = Responses["actions/runs/200"].DeepClone() });

    internal GitHubEvidence GitHub() =>
        new(this, Identity, Policy, TimeProvider.System, DelayAsync);

    internal MainSonarPolicy Sonar() => new(this, Policy, GateName, "scanner", "verifier");

    internal JsonNode Gate() =>
        Node(
            new
            {
                status = "OK",
                ignoredConditions = false,
                conditions = Policy.Conditions.Select(c => new
                {
                    metricKey = c.Metric,
                    comparator = c.Op,
                    errorThreshold = c.Error,
                    status = "OK",
                    actualValue = c.Op == "LT" ? "100" : "0",
                }),
            }
        );

    internal AnalysisSummary Summary() =>
        new()
        {
            Project = MainPolicy.Project,
            ServerId = MainPolicy.ServerId,
            Sdk = "10.0.402",
            StartedAt = Now.AddMinutes(-1),
            AnalysisId = "analysis-id",
            Gate = Element(Gate()),
        };

    internal JsonNode Latest() =>
        Node(
            new
            {
                revision = Sha,
                key = "analysis-id",
                buildString = Identity.BuildString,
                date = Now,
            }
        );

    public Task<JsonElement> GetAsync(string path, CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        if (Failure is not null)
            throw Failure;

        Requests.Add(path);
        return Task.FromResult(Element(Responses[path]));
    }

    public Task<JsonElement> GetAsync(
        string endpoint,
        string? token,
        CancellationToken cancellationToken,
        params (string Key, string Value)[] query
    ) => GetAsync(endpoint, cancellationToken);

    private Task DelayAsync(TimeSpan delay, CancellationToken cancellationToken)
    {
        Delays++;
        OnDelay?.Invoke();

        return Task.CompletedTask;
    }

    internal static JsonNode Node(object value) =>
        JsonSerializer.SerializeToNode(value, JsonFormat.Options)!;

    internal static JsonElement Element(JsonNode value) => JsonSerializer.SerializeToElement(value);
}
