using System.Net;

namespace AndreGoepel.AppFoundation.Sonar;

internal sealed class NuGetUploadTransport(
    Uri endpoint,
    NuGetUploadCredential credential,
    TimeProvider clock,
    CancellationToken operationToken
) : DelegatingHandler
{
    private int sent;

    internal bool Started => sent != 0;
    internal bool Acknowledged { get; private set; }

    protected override async Task<HttpResponseMessage> SendAsync(
        HttpRequestMessage request,
        CancellationToken cancellationToken
    )
    {
        using var linked = CancellationTokenSource.CreateLinkedTokenSource(
            operationToken,
            cancellationToken
        );
        linked.Token.ThrowIfCancellationRequested();
        if (
            request.Method != HttpMethod.Put
            || request.RequestUri != endpoint
            || !credential.IsUsable(clock.GetUtcNow())
            || Interlocked.CompareExchange(ref sent, 1, 0) != 0
        )
            throw new InvalidOperationException("The upload transport refused this request.");

        var response = await base.SendAsync(request, linked.Token);
        Acknowledged = response.StatusCode is HttpStatusCode.Created or HttpStatusCode.Accepted;
        return response;
    }
}
