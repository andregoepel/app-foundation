using System.Net.Http.Headers;
using System.Text;
using System.Text.Json;

namespace AndreGoepel.AppFoundation.Sonar;

internal interface ISonarClient
{
    Task<JsonElement> GetAsync(
        string endpoint,
        string? token,
        CancellationToken cancellationToken,
        params (string Key, string Value)[] query
    );
}

internal sealed class SonarClient(HttpClient client, Uri url) : ISonarClient
{
    internal static HttpClient CreateHttpClient() =>
        new(new HttpClientHandler { AllowAutoRedirect = false, UseProxy = false })
        {
            Timeout = TimeSpan.FromSeconds(30),
        };

    public async Task<JsonElement> GetAsync(
        string endpoint,
        string? token,
        CancellationToken cancellationToken,
        params (string Key, string Value)[] query
    )
    {
        var parameters = string.Join(
            "&",
            query.Select(pair =>
                Uri.EscapeDataString(pair.Key) + "=" + Uri.EscapeDataString(pair.Value)
            )
        );
        using var request = new HttpRequestMessage(
            HttpMethod.Get,
            new Uri(url, "api/" + endpoint + "?" + parameters)
        );
        if (!string.IsNullOrEmpty(token))
            request.Headers.Authorization = new AuthenticationHeaderValue(
                "Basic",
                Convert.ToBase64String(Encoding.UTF8.GetBytes(token + ":"))
            );

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
