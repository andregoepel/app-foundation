using System.Text.Json;
using Octokit;
using Octokit.Internal;

namespace AndreGoepel.AppFoundation.Sonar;

internal interface IGitHubClient
{
    Task<JsonElement> GetAsync(string path, CancellationToken cancellationToken);
}

internal sealed class GitHubClient : IGitHubClient, IDisposable
{
    private static readonly Uri RepositoryUrl = new(
        $"https://api.github.com/repos/{MainPolicy.Repository}/"
    );
    private readonly HttpClientAdapter transport;
    private readonly Connection connection;

    internal GitHubClient(string token, HttpMessageHandler? handler = null)
    {
        transport = new HttpClientAdapter(() => new RequestPolicy(handler));
        transport.SetRequestTimeout(TimeSpan.FromSeconds(30));
        connection = new Connection(
            new ProductHeaderValue("app-foundation-sonar", "1.0"),
            Octokit.GitHubClient.GitHubApiUrl,
            new InMemoryCredentialStore(new Credentials(token, AuthenticationType.Bearer)),
            transport,
            new EvidenceSerializer()
        );
    }

    public async Task<JsonElement> GetAsync(string path, CancellationToken cancellationToken)
    {
        var url = new Uri(RepositoryUrl, path);
        if (!RepositoryUrl.IsBaseOf(url) || url.Fragment.Length != 0)
            throw new InvalidDataException(
                "GitHub evidence must belong to the expected repository."
            );

        using var timeout = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        timeout.CancelAfter(TimeSpan.FromSeconds(30));

        try
        {
            var response = await connection.Get<JsonElement>(
                url,
                null,
                "application/vnd.github+json",
                timeout.Token
            );
            if (response.Body.ValueKind != JsonValueKind.Object)
                throw new InvalidDataException("GitHub returned an invalid evidence document.");

            return response.Body;
        }
        catch (ApiException error)
        {
            // API error bodies can contain private request data; keep them out of CLI output.
            throw new HttpRequestException(
                "GitHub evidence request failed.",
                null,
                error.StatusCode
            );
        }
    }

    public void Dispose() => transport.Dispose();

    private sealed class RequestPolicy : DelegatingHandler
    {
        internal RequestPolicy(HttpMessageHandler? handler)
            : base(handler ?? new HttpClientHandler { AllowAutoRedirect = false, UseProxy = false })
        { }

        protected override async Task<HttpResponseMessage> SendAsync(
            HttpRequestMessage request,
            CancellationToken cancellationToken
        )
        {
            request.Headers.Add("X-GitHub-Api-Version", "2022-11-28");
            var response = await base.SendAsync(request, cancellationToken);

            try
            {
                // Octokit follows redirects above HttpClientHandler, so reject them here.
                if ((int)response.StatusCode is >= 300 and < 400)
                    throw new HttpRequestException("GitHub evidence redirects are not allowed.");

                await response.Content.LoadIntoBufferAsync(4 * 1024 * 1024, cancellationToken);
                return response;
            }
            catch
            {
                response.Dispose();
                throw;
            }
        }
    }

    private sealed class EvidenceSerializer : IJsonSerializer
    {
        public string Serialize(object item) => JsonSerializer.Serialize(item);

        public T Deserialize<T>(string json) => JsonSerializer.Deserialize<T>(json)!;
    }
}
