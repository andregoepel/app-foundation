using System.Net;
using System.Text;
using System.Text.Json;
using AndreGoepel.AppFoundation.Sonar;
using Xunit;

namespace AndreGoepel.AppFoundation.Sonar.Tests;

public sealed class ApiClientContractTests
{
    [Fact]
    public async Task GitHub_EvidenceFields_PreservesRawNamesAndMissingFields()
    {
        // Arrange
        const string json = """
            {"total_count":101,"artifacts":[{"digest":"sha256:exact","workflow_run":{"head_sha":"revision"}}],
            "run_attempt":3,"referenced_workflows":[{"path":"reviewed.yml@main","sha":"shared-revision"}]}
            """;
        using var handler = new ResponseHandler(HttpStatusCode.OK, json);
        using var client = new GitHubClient("read-token", handler);

        // Act
        var result = await client.GetAsync(
            "actions/runs/123/artifacts?per_page=100",
            TestContext.Current.CancellationToken
        );

        // Assert
        Assert.Equal(101, result.GetProperty("total_count").GetInt32());
        Assert.Equal(
            "sha256:exact",
            result.GetProperty("artifacts")[0].GetProperty("digest").GetString()
        );
        Assert.Equal(
            "shared-revision",
            result.GetProperty("referenced_workflows")[0].GetProperty("sha").GetString()
        );
        Assert.Equal(3, result.GetProperty("run_attempt").GetInt32());
        Assert.False(result.TryGetProperty("conclusion", out _));
        Assert.Equal("2022-11-28", handler.ApiVersion);
        Assert.Contains("app-foundation-sonar/1.0", handler.UserAgent);
        Assert.Equal(1, handler.Requests);
    }

    [Theory]
    [InlineData(301)]
    [InlineData(302)]
    [InlineData(307)]
    [InlineData(308)]
    public async Task GitHub_Redirect_StopsBeforeFollowingLocation(int status)
    {
        // Arrange
        using var handler = new ResponseHandler((HttpStatusCode)status, "{}")
        {
            Location = "https://api.github.com/repos/other/private",
        };
        using var client = new GitHubClient("read-token", handler);

        // Act / Assert
        await Assert.ThrowsAsync<HttpRequestException>(() =>
            client.GetAsync("actions/runs/123", TestContext.Current.CancellationToken)
        );
        Assert.Equal(1, handler.Requests);
    }

    [Theory]
    [InlineData(401)]
    [InlineData(403)]
    [InlineData(404)]
    [InlineData(429)]
    [InlineData(500)]
    public async Task GitHub_ApiError_FailsWithoutRetryOrLeakingResponse(int status)
    {
        // Arrange
        using var handler = new ResponseHandler(
            (HttpStatusCode)status,
            "{\"message\":\"private-server-detail\"}"
        );
        using var client = new GitHubClient("read-token", handler);

        // Act
        var error = await Assert.ThrowsAsync<HttpRequestException>(() =>
            client.GetAsync("actions/runs/123", TestContext.Current.CancellationToken)
        );

        // Assert
        Assert.Equal((HttpStatusCode)status, error.StatusCode);
        Assert.DoesNotContain("private-server-detail", error.ToString());
        Assert.DoesNotContain("read-token", error.ToString());
        Assert.Equal(1, handler.Requests);
    }

    [Theory]
    [InlineData("../other/actions/runs/123")]
    [InlineData("https://evil.example/metadata")]
    [InlineData("actions/runs/123#fragment")]
    public async Task GitHub_OutsideRepository_RejectsBeforeSending(string path)
    {
        // Arrange
        using var handler = new ResponseHandler(HttpStatusCode.OK, "{\"ok\":true}");
        using var client = new GitHubClient("read-token", handler);

        // Act / Assert
        await Assert.ThrowsAsync<InvalidDataException>(() =>
            client.GetAsync(path, TestContext.Current.CancellationToken)
        );
        Assert.Equal(0, handler.Requests);
    }

    [Theory]
    [InlineData("[]")]
    [InlineData("null")]
    public async Task Clients_NonObjectJson_RejectEvidence(string json)
    {
        // Arrange
        using var githubHandler = new ResponseHandler(HttpStatusCode.OK, json);
        using var github = new GitHubClient("read-token", githubHandler);
        using var sonarHttp = new HttpClient(new ResponseHandler(HttpStatusCode.OK, json));
        var sonar = new SonarClient(sonarHttp, new Uri("http://127.0.0.1:9000"));

        // Act / Assert
        await Assert.ThrowsAsync<InvalidDataException>(() =>
            github.GetAsync("git/ref/heads/main", TestContext.Current.CancellationToken)
        );
        await Assert.ThrowsAsync<InvalidDataException>(() =>
            sonar.GetAsync("system/status", null, TestContext.Current.CancellationToken)
        );
    }

    [Fact]
    public async Task Clients_MalformedJson_RejectEvidence()
    {
        // Arrange
        using var githubHandler = new ResponseHandler(HttpStatusCode.OK, "{broken");
        using var github = new GitHubClient("read-token", githubHandler);
        using var sonarHttp = new HttpClient(new ResponseHandler(HttpStatusCode.OK, "{broken"));
        var sonar = new SonarClient(sonarHttp, new Uri("http://127.0.0.1:9000"));

        // Act / Assert
        await Assert.ThrowsAnyAsync<JsonException>(() =>
            github.GetAsync("git/ref/heads/main", TestContext.Current.CancellationToken)
        );
        await Assert.ThrowsAnyAsync<JsonException>(() =>
            sonar.GetAsync("system/status", null, TestContext.Current.CancellationToken)
        );
    }

    [Fact]
    public async Task GitHub_OversizedResponse_StopsAtBound()
    {
        // Arrange
        using var handler = new ResponseHandler(
            HttpStatusCode.OK,
            "{\"large\":\"" + new string('x', 4 * 1024 * 1024) + "\"}"
        );
        using var client = new GitHubClient("read-token", handler);

        // Act / Assert
        await Assert.ThrowsAsync<HttpRequestException>(() =>
            client.GetAsync("actions/runs/123", TestContext.Current.CancellationToken)
        );
        Assert.Equal(1, handler.Requests);
    }

    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public async Task Clients_CancelledRequest_CancelsUnderlyingSend(bool useGitHub)
    {
        // Arrange
        using var cancellation = CancellationTokenSource.CreateLinkedTokenSource(
            TestContext.Current.CancellationToken
        );
        using var handler = new WaitingHandler();
        using var github = useGitHub ? new GitHubClient("read-token", handler) : null;
        using var http = useGitHub ? null : new HttpClient(handler);
        var sonar = http is null ? null : new SonarClient(http, new Uri("http://127.0.0.1:9000"));

        // Act
        var request = useGitHub
            ? github!.GetAsync("git/ref/heads/main", cancellation.Token)
            : sonar!.GetAsync("system/status", null, cancellation.Token);
        await handler.Entered.Task.WaitAsync(TestContext.Current.CancellationToken);
        await cancellation.CancelAsync();

        // Assert
        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => request);
        Assert.True(handler.CancellationObserved);
    }

    [Fact]
    public async Task GitHub_CancelledResponseBody_CancelsBuffering()
    {
        // Arrange
        using var cancellation = CancellationTokenSource.CreateLinkedTokenSource(
            TestContext.Current.CancellationToken
        );
        using var body = new WaitingContent();
        using var handler = new ResponseHandler(HttpStatusCode.OK, "") { Body = body };
        using var client = new GitHubClient("read-token", handler);

        // Act
        var request = client.GetAsync("git/ref/heads/main", cancellation.Token);
        await body.Entered.Task.WaitAsync(TestContext.Current.CancellationToken);
        await cancellation.CancelAsync();

        // Assert
        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => request);
        Assert.True(body.CancellationObserved);
    }

    [Fact]
    public async Task Sonar_QueryAndCredentials_EncodeParametersWithoutChangingToken()
    {
        // Arrange
        using var handler = new ResponseHandler(HttpStatusCode.OK, "{\"status\":\"UP\"}");
        using var http = new HttpClient(handler);
        var sonar = new SonarClient(http, new Uri("http://127.0.0.1:9000"));

        // Act
        await sonar.GetAsync(
            "qualitygates/show",
            "read-token",
            TestContext.Current.CancellationToken,
            ("name", "Gate + & /")
        );

        // Assert
        Assert.Equal(
            "http://127.0.0.1:9000/api/qualitygates/show?name=Gate%20%2B%20%26%20%2F",
            handler.Url
        );
        Assert.Equal(
            "Basic " + Convert.ToBase64String(Encoding.UTF8.GetBytes("read-token:")),
            handler.Authorization
        );
    }

    private sealed class ResponseHandler(HttpStatusCode status, string json) : HttpMessageHandler
    {
        internal int Requests { get; private set; }
        internal string? Location { get; init; }
        internal HttpContent? Body { get; init; }
        internal string? Url { get; private set; }
        internal string? Authorization { get; private set; }
        internal string? ApiVersion { get; private set; }
        internal string UserAgent { get; private set; } = "";

        protected override Task<HttpResponseMessage> SendAsync(
            HttpRequestMessage request,
            CancellationToken cancellationToken
        )
        {
            Requests++;
            Url = request.RequestUri!.AbsoluteUri;
            Authorization = request.Headers.Authorization?.ToString();
            ApiVersion = request.Headers.TryGetValues("X-GitHub-Api-Version", out var values)
                ? values.Single()
                : null;
            UserAgent = request.Headers.UserAgent.ToString();
            var response = new HttpResponseMessage(status)
            {
                Content = Body ?? new StringContent(json, Encoding.UTF8, "application/json"),
            };
            if (Location is not null)
                response.Headers.Location = new Uri(Location);
            return Task.FromResult(response);
        }
    }

    private sealed class WaitingContent : HttpContent
    {
        internal TaskCompletionSource Entered { get; } =
            new(TaskCreationOptions.RunContinuationsAsynchronously);
        internal bool CancellationObserved { get; private set; }

        protected override Task SerializeToStreamAsync(Stream stream, TransportContext? context) =>
            SerializeToStreamAsync(stream, context, CancellationToken.None);

        protected override async Task SerializeToStreamAsync(
            Stream stream,
            TransportContext? context,
            CancellationToken cancellationToken
        )
        {
            Entered.SetResult();
            try
            {
                await Task.Delay(Timeout.InfiniteTimeSpan, cancellationToken);
            }
            catch (OperationCanceledException)
            {
                CancellationObserved = true;
                throw;
            }
        }

        protected override bool TryComputeLength(out long length)
        {
            length = 0;
            return false;
        }
    }

    private sealed class WaitingHandler : HttpMessageHandler
    {
        internal TaskCompletionSource Entered { get; } =
            new(TaskCreationOptions.RunContinuationsAsynchronously);
        internal bool CancellationObserved { get; private set; }

        protected override async Task<HttpResponseMessage> SendAsync(
            HttpRequestMessage request,
            CancellationToken cancellationToken
        )
        {
            Entered.SetResult();
            try
            {
                await Task.Delay(Timeout.InfiniteTimeSpan, cancellationToken);
                throw new InvalidOperationException("Expected cancellation.");
            }
            catch (OperationCanceledException)
            {
                CancellationObserved = true;
                throw;
            }
        }
    }
}
