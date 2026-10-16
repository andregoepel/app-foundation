using System.Net.Http.Headers;
using System.Text.Json;

namespace AndreGoepel.AppFoundation.Sonar;

internal interface IGitHubClient
{
    Task<JsonElement> GetAsync(string path, CancellationToken cancellationToken);
}

internal sealed class GitHubClient(HttpClient client, string token) : IGitHubClient
{
    public async Task<JsonElement> GetAsync(string path, CancellationToken cancellationToken)
    {
        using var request = new HttpRequestMessage(
            HttpMethod.Get,
            $"https://api.github.com/repos/{MainPolicy.Repository}/{path}"
        );
        request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", token);
        request.Headers.UserAgent.ParseAdd("app-foundation-sonar/1.0");
        request.Headers.Accept.ParseAdd("application/vnd.github+json");
        request.Headers.Add("X-GitHub-Api-Version", "2022-11-28");

        using var response = await client.SendAsync(request, cancellationToken);
        response.EnsureSuccessStatusCode();

        await using var stream = await response.Content.ReadAsStreamAsync(cancellationToken);
        using var document = await JsonDocument.ParseAsync(
            stream,
            cancellationToken: cancellationToken
        );

        return document.RootElement.Clone();
    }
}
