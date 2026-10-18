using System.IO.Compression;
using System.Net;
using System.Text;
using System.Text.Json.Nodes;
using AndreGoepel.AppFoundation.Sonar;
using AndreGoepel.Core;
using Xunit;

namespace AndreGoepel.AppFoundation.Sonar.Tests;

internal sealed class NuGetAcceptanceFixture : HttpMessageHandler, IProcessRunner
{
    private const string GitHubPrefix = "/repos/" + MainPolicy.Repository + "/";
    private static readonly string[] BuildCommands =
    [
        "clean",
        "restore",
        "msbuild",
        "build",
        "list",
        "test",
        "pack",
    ];
    private readonly HttpClient http;
    private readonly Dictionary<string, byte[]> archives = new(StringComparer.Ordinal);

    internal ReleaseFixture Evidence { get; } = new();
    internal NuGetPackageBuilderTests.BuildRunner Builder { get; }
    internal AcceptanceClock Clock { get; }
    internal List<string> Events { get; } = [];
    internal List<string> Commands { get; } = [];
    internal Action<string>? OnRequest { get; set; }
    internal string? FailedEndpoint { get; set; }
    internal HttpStatusCode FailureStatus { get; set; } = HttpStatusCode.ServiceUnavailable;
    internal string? MalformedEndpoint { get; set; }
    internal string? CancelledEndpoint { get; set; }
    internal string StorageHost { get; set; } = "fixture.blob.core.windows.net";
    internal int PackageCommands => Builder.Commands.Count(args => args[1] == "pack");

    internal NuGetAcceptanceFixture(string buildFault = "")
    {
        http = new HttpClient(this, disposeHandler: false);
        Builder = new(Evidence.Release, buildFault);
        Clock = new(Evidence.Main.Now);
    }

    internal async Task InitializeAsync(CancellationToken ct)
    {
        await Evidence.InitializeAsync(ct);
        Events.Clear();
        SealArtifacts();
    }

    internal void SealArtifacts()
    {
        archives["1"] = Archive("readiness.json", Evidence.Receipt);
        archives["2"] = Archive("summary.json", Evidence.Summary);
        foreach (var item in (JsonArray)Evidence.ArtifactInventory["artifacts"]!)
        {
            var bytes = archives[item!["id"]!.ToString()];
            item["digest"] = "sha256:" + SourceFiles.Hash(bytes);
            item["size_in_bytes"] = bytes.Length;
        }
    }

    internal void CorruptArchive() => archives["1"] = [0, 1, 2];

    internal void SetTag(string sha) =>
        Evidence.Main.Responses["git/ref/tags/v2.2.0"] = MainFixture.Node(
            new { @object = new { type = "commit", sha } }
        );

    internal Task<Result<string>> RunAsync(CancellationToken ct, bool tagged = false)
    {
        var readiness = new ReleaseReadiness(
            new GitHubClient(http, "github-fixture"),
            new GitHubArtifacts(http, "github-fixture"),
            new SonarClient(http, new Uri(MainPolicy.SonarUrl)),
            this,
            Clock,
            Evidence.Main.Policy,
            Evidence.Contract,
            "scan-fixture",
            "verify-fixture"
        );

        return new NuGetDryRun(
            readiness,
            new NuGetPackageBuilder(this, Evidence.Contract)
        ).RunAsync(Evidence.Release with { Tag = tagged ? "v2.2.0" : null }, ct);
    }

    internal string[] Manifests() =>
        Directory.GetFiles(Evidence.Root, "dry-run.json", SearchOption.AllDirectories);

    internal void AssertNoPublication()
    {
        Assert.DoesNotContain(
            Commands,
            command => command.Contains("push", StringComparison.Ordinal)
        );
        Assert.DoesNotContain(
            Commands,
            command => command.Contains("login", StringComparison.Ordinal)
        );
        Assert.All(
            Events.Where(entry => entry.StartsWith("http:", StringComparison.Ordinal)),
            entry => Assert.StartsWith("http:GET:", entry)
        );
        Assert.DoesNotContain(
            Events,
            entry => entry.Contains("nuget.org", StringComparison.Ordinal)
        );
        Assert.DoesNotContain(
            Events,
            entry => entry.Contains("idtoken", StringComparison.OrdinalIgnoreCase)
        );
    }

    public async Task<Result<CommandOutput>> RunAsync(
        string root,
        string[] arguments,
        CancellationToken ct
    )
    {
        var command = string.Join(' ', arguments);
        Commands.Add(command);
        Events.Add("command:" + command);
        Assert.True(arguments[0] is "git" or "dotnet", command);
        if (arguments[0] == "git")
            return await Evidence.RunAsync(root, arguments, ct);

        Assert.Contains(arguments[1], BuildCommands);
        return await Builder.RunAsync(root, arguments, ct);
    }

    protected override Task<HttpResponseMessage> SendAsync(
        HttpRequestMessage request,
        CancellationToken ct
    )
    {
        ct.ThrowIfCancellationRequested();
        var uri = request.RequestUri!;
        Events.Add($"http:{request.Method}:{uri.Host}{uri.PathAndQuery}");
        Assert.Equal(HttpMethod.Get, request.Method);
        var key = uri.Host switch
        {
            "api.github.com" => uri.PathAndQuery[GitHubPrefix.Length..],
            "sonar.andregoepel.dev" => uri.AbsolutePath["/api/".Length..],
            "fixture.blob.core.windows.net" => uri.AbsolutePath.TrimStart('/'),
            _ => throw new InvalidOperationException(
                "Unexpected outbound destination: " + uri.Host
            ),
        };
        OnRequest?.Invoke(key);
        if (key == CancelledEndpoint)
            throw new OperationCanceledException("Fixture transport cancelled.", ct);
        if (key == FailedEndpoint)
            return Task.FromResult(new HttpResponseMessage(FailureStatus));
        if (key == MalformedEndpoint)
            return Task.FromResult(JsonResponse("{malformed"));

        if (uri.Host == "fixture.blob.core.windows.net")
        {
            Assert.Null(request.Headers.Authorization);
            return Task.FromResult(
                new HttpResponseMessage(HttpStatusCode.OK)
                {
                    Content = new ByteArrayContent(archives[key]),
                }
            );
        }

        if (uri.Host == "api.github.com")
        {
            Assert.StartsWith(GitHubPrefix, uri.AbsolutePath);
            Assert.Equal("Bearer github-fixture", request.Headers.Authorization?.ToString());
            if (key.StartsWith("actions/artifacts/", StringComparison.Ordinal))
                return Task.FromResult(
                    new HttpResponseMessage(HttpStatusCode.Redirect)
                    {
                        Headers =
                        {
                            Location = new Uri($"https://{StorageHost}/{key.Split('/')[2]}"),
                        },
                    }
                );
        }
        else
        {
            var token = key is "ce/task" or "settings/values" or "new_code_periods/show"
                ? "scan-fixture"
                : "verify-fixture";
            if (key == "system/status")
                Assert.Null(request.Headers.Authorization);
            else
                Assert.Equal(
                    "Basic " + Convert.ToBase64String(Encoding.UTF8.GetBytes(token + ":")),
                    request.Headers.Authorization?.ToString()
                );
        }

        return Task.FromResult(JsonResponse(Evidence.Main.Responses[key].ToJsonString()));
    }

    private static HttpResponseMessage JsonResponse(string json) =>
        new(HttpStatusCode.OK)
        {
            Content = new StringContent(json, Encoding.UTF8, "application/json"),
        };

    private static byte[] Archive(string filename, JsonNode value)
    {
        using var stream = new MemoryStream();
        using (var archive = new ZipArchive(stream, ZipArchiveMode.Create, leaveOpen: true))
        using (
            var writer = new StreamWriter(
                archive.CreateEntry(filename).Open(),
                new UTF8Encoding(false)
            )
        )
            writer.Write(Encoding.UTF8.GetString(ReleaseFixture.Bytes(value)));

        return stream.ToArray();
    }

    protected override void Dispose(bool disposing)
    {
        if (disposing)
        {
            http.Dispose();
            Evidence.Dispose();
        }
        base.Dispose(disposing);
    }

    internal sealed class AcceptanceClock(DateTimeOffset now) : TimeProvider
    {
        internal DateTimeOffset Now { get; set; } = now;

        public override DateTimeOffset GetUtcNow() => Now;
    }
}
