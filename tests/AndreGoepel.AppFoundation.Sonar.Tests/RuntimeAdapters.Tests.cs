using System.Net;
using System.Text;
using AndreGoepel.AppFoundation.Sonar;
using Xunit;

namespace AndreGoepel.AppFoundation.Sonar.Tests;

public sealed class RuntimeAdaptersTests
{
    [Fact]
    public async Task Process_LocalDotnetCommand_CapturesExitAndOutput()
    {
        // Arrange
        var runner = new ProcessRunner();
        // Act
        var result = await runner.RunAsync(
            Directory.GetCurrentDirectory(),
            ["dotnet", "--version"],
            TestContext.Current.CancellationToken
        );
        // Assert
        Assert.True(result.IsSuccess, result.Error);
        Assert.Equal(0, result.Value!.ExitCode);
        Assert.StartsWith("10.", result.Value.Text);
    }

    [Fact]
    public async Task Process_MissingExecutable_ReturnsSanitizedFailure()
    {
        // Act
        var result = await new ProcessRunner().RunAsync(
            Path.GetTempPath(),
            ["missing-sonar-command-" + Guid.NewGuid().ToString("N"), "secret"],
            TestContext.Current.CancellationToken
        );
        // Assert
        Assert.True(result.IsFailure);
        Assert.DoesNotContain("secret", result.Error);
    }

    [Fact]
    public void Process_ArgumentsAndEnvironment_PreventShellExpansionAndCredentialInheritance()
    {
        // Act
        var start = ProcessRunner.CreateStartInfo(
            Path.GetTempPath(),
            ["dotnet", "a path", "$(not-a-command)"]
        );
        // Assert
        Assert.False(start.UseShellExecute);
        Assert.Equal(["a path", "$(not-a-command)"], start.ArgumentList);
        Assert.False(start.Environment.ContainsKey("APPFOUNDATION_SONAR_TOKEN"));
        Assert.False(start.Environment.ContainsKey("APPFOUNDATION_SONAR_VERIFY_TOKEN"));
    }

    [Theory]
    [InlineData(HttpStatusCode.OK)]
    [InlineData(HttpStatusCode.Redirect)]
    [InlineData(HttpStatusCode.Unauthorized)]
    public async Task Sonar_Response_ParsesSuccessAndRejectsRedirectOrFailure(HttpStatusCode status)
    {
        // Arrange
        using var handler = new StubHandler(status);
        using var http = new HttpClient(handler);
        var client = new SonarClient(http, new Uri("http://127.0.0.1:9000"));
        // Act / Assert
        if (status == HttpStatusCode.OK)
        {
            var response = await client.GetAsync(
                "system/status",
                "local-token",
                TestContext.Current.CancellationToken,
                ("key", "space & value")
            );
            Assert.Equal("UP", response.GetProperty("status").GetString());
        }
        else
            await Assert.ThrowsAsync<HttpRequestException>(() =>
                client.GetAsync(
                    "system/status",
                    "local-token",
                    TestContext.Current.CancellationToken,
                    ("key", "space & value")
                )
            );
        Assert.Equal(
            "http://127.0.0.1:9000/api/system/status?key=space%20%26%20value",
            handler.Url
        );
        Assert.Equal(
            Convert.ToBase64String(Encoding.UTF8.GetBytes("local-token:")),
            handler.Authorization
        );
    }

    private sealed class StubHandler(HttpStatusCode status) : HttpMessageHandler
    {
        internal string? Url { get; private set; }
        internal string? Authorization { get; private set; }

        protected override Task<HttpResponseMessage> SendAsync(
            HttpRequestMessage request,
            CancellationToken cancellationToken
        )
        {
            Url = request.RequestUri!.AbsoluteUri;
            Authorization = request.Headers.Authorization?.Parameter;
            return Task.FromResult(
                new HttpResponseMessage(status)
                {
                    Content = new StringContent("{\"status\":\"UP\"}"),
                }
            );
        }
    }
}
