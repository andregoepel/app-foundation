using System.Net;
using System.Text;
using AndreGoepel.AppFoundation.Sonar;
using AndreGoepel.Core;
using Xunit;

namespace AndreGoepel.AppFoundation.Sonar.Tests;

public sealed class MainCommandTests
{
    [Theory]
    [InlineData("none", true)]
    [InlineData("wrong-head", false)]
    [InlineData("head-command", false)]
    [InlineData("dirty", false)]
    [InlineData("untracked", false)]
    [InlineData("status-command", false)]
    public async Task CheckCheckout_RequiresExactCleanSource(string fault, bool accepted)
    {
        // Arrange
        var process = new GitProcess(fault);

        // Act
        var result = await MainCommand.CheckCheckoutAsync(
            process,
            Path.GetTempPath(),
            MainFixture.Sha,
            TestContext.Current.CancellationToken
        );

        // Assert
        Assert.Equal(accepted, result.IsSuccess);
    }

    [Theory]
    [InlineData("\n", true)]
    [InlineData("\r\n", true)]
    [InlineData("changed", false)]
    public async Task ValidateConfiguration_WorkflowFingerprint_NormalizesOnlyLineEndings(
        string lineEnding,
        bool accepted
    )
    {
        // Arrange
        using var directory = new RunnerFixture();
        var fixture = new MainFixture();
        var hash = SourceFiles.Hash(Encoding.UTF8.GetBytes("name: CI\n"));
        var policy = fixture.Policy with
        {
            Workflows = fixture
                .Policy.Workflows.Select(w => w with { FileSha256 = hash })
                .ToArray(),
        };
        foreach (var workflow in policy.Workflows)
            directory.Write(workflow.Path, "name: CI" + lineEnding);

        // Act
        var result = await MainCommand.ValidateConfigurationAsync(
            directory.Root,
            policy,
            TestContext.Current.CancellationToken
        );

        // Assert
        Assert.Equal(accepted, result.IsSuccess);
    }

    [Theory]
    [InlineData(0)]
    [InlineData(1441)]
    public async Task ValidateConfiguration_UnboundedEvidenceAge_Rejects(int age)
    {
        // Arrange
        var fixture = new MainFixture();

        // Act
        var result = await MainCommand.ValidateConfigurationAsync(
            Path.GetTempPath(),
            fixture.Policy with
            {
                MaximumRunAgeMinutes = age,
            },
            TestContext.Current.CancellationToken
        );

        // Assert
        Assert.True(result.IsFailure);
    }

    [Fact]
    public async Task GitHubClient_Request_UsesPinnedApiAndReadToken()
    {
        // Arrange
        using var handler = new GitHubHandler(HttpStatusCode.OK);
        using var http = new HttpClient(handler);
        var client = new GitHubClient(http, "test-token");

        // Act
        var result = await client.GetAsync(
            "git/ref/heads/main",
            TestContext.Current.CancellationToken
        );

        // Assert
        Assert.Equal(
            "https://api.github.com/repos/andregoepel/app-foundation/git/ref/heads/main",
            handler.Url
        );
        Assert.Equal("Bearer test-token", handler.Authorization);
        Assert.True(result.GetProperty("ok").GetBoolean());
    }

    [Theory]
    [InlineData(HttpStatusCode.Unauthorized)]
    [InlineData(HttpStatusCode.Forbidden)]
    [InlineData(HttpStatusCode.InternalServerError)]
    [InlineData(HttpStatusCode.Redirect)]
    public async Task GitHubClient_ApiFailure_RejectsEvidence(HttpStatusCode status)
    {
        // Arrange
        using var handler = new GitHubHandler(status);
        using var http = new HttpClient(handler);
        var client = new GitHubClient(http, "test-token");

        // Act / Assert
        await Assert.ThrowsAsync<HttpRequestException>(() =>
            client.GetAsync("git/ref/heads/main", TestContext.Current.CancellationToken)
        );
    }

    private sealed class GitProcess(string fault) : IProcessRunner
    {
        public Task<Result<CommandOutput>> RunAsync(
            string root,
            string[] arguments,
            CancellationToken cancellationToken
        )
        {
            var head = arguments.Contains("rev-parse");
            if ((head && fault == "head-command") || (!head && fault == "status-command"))
                return Task.FromResult(Result.Fail<CommandOutput>("Git failed."));

            var text = head
                ? (fault == "wrong-head" ? "other" : MainFixture.Sha)
                : fault switch
                {
                    "dirty" => " M source.cs",
                    "untracked" => "?? injected.cs",
                    _ => "",
                };

            return Task.FromResult(Result.Ok(new CommandOutput(0, text)));
        }
    }

    private sealed class GitHubHandler(HttpStatusCode status) : HttpMessageHandler
    {
        internal string? Url { get; private set; }
        internal string? Authorization { get; private set; }

        protected override Task<HttpResponseMessage> SendAsync(
            HttpRequestMessage request,
            CancellationToken cancellationToken
        )
        {
            Url = request.RequestUri!.AbsoluteUri;
            Authorization = request.Headers.Authorization!.ToString();

            return Task.FromResult(
                new HttpResponseMessage(status) { Content = new StringContent("{\"ok\":true}") }
            );
        }
    }
}
