using System.Net;
using System.Text;
using System.Text.Json;
using System.Text.RegularExpressions;

namespace AndreGoepel.AppFoundation.E2ETests.Infrastructure;

/// <summary>
/// Thin client over the MailHog HTTP API so tests can assert on emails the app actually delivered
/// (confirmation links, password-reset links, etc.).
/// </summary>
public sealed partial class MailHogClient(string baseUrl)
{
    private readonly HttpClient _http = new() { BaseAddress = new Uri(baseUrl) };

    /// <summary>Deletes every message so a test starts from a known-empty inbox.</summary>
    public async Task ClearAsync(CancellationToken ct = default)
    {
        using var response = await _http.DeleteAsync("api/v1/messages", ct);
        response.EnsureSuccessStatusCode();
    }

    /// <summary>
    /// Polls until an email addressed to <paramref name="toEmail"/> arrives, returning its decoded body.
    /// </summary>
    public async Task<string> WaitForMessageBodyAsync(
        string toEmail,
        TimeSpan? timeout = null,
        CancellationToken ct = default
    )
    {
        var deadline = DateTime.UtcNow + (timeout ?? TimeSpan.FromSeconds(30));

        while (DateTime.UtcNow < deadline)
        {
            var body = await TryGetLatestBodyAsync(toEmail, ct);
            if (body is not null)
            {
                return body;
            }

            await Task.Delay(500, ct);
        }

        throw new TimeoutException($"No email delivered to '{toEmail}' within the timeout.");
    }

    /// <summary>Waits for the newest email to <paramref name="toEmail"/> and extracts the first link matching a filter.</summary>
    public async Task<string> WaitForLinkAsync(
        string toEmail,
        string mustContain,
        TimeSpan? timeout = null,
        CancellationToken ct = default
    )
    {
        var body = await WaitForMessageBodyAsync(toEmail, timeout, ct);
        var links = LinkRegex()
            .Matches(body)
            .Select(m => WebUtility.HtmlDecode(m.Value))
            .Where(url => url.Contains(mustContain, StringComparison.OrdinalIgnoreCase))
            .ToList();

        if (links.Count == 0)
        {
            throw new InvalidOperationException(
                $"No link containing '{mustContain}' found in email to '{toEmail}'. Body was:\n{body}"
            );
        }

        return links[0];
    }

    private async Task<string?> TryGetLatestBodyAsync(string toEmail, CancellationToken ct)
    {
        var json = await _http.GetStringAsync(
            "api/v2/search?kind=to&query=" + Uri.EscapeDataString(toEmail),
            ct
        );

        using var doc = JsonDocument.Parse(json);

        if (!doc.RootElement.TryGetProperty("items", out var items) || items.GetArrayLength() == 0)
        {
            return null;
        }

        // MailHog returns newest first.
        var message = items[0];
        return DecodeBody(message);
    }

    private static string DecodeBody(JsonElement message)
    {
        var builder = new StringBuilder();

        if (
            message.TryGetProperty("Content", out var content)
            && content.TryGetProperty("Body", out var body)
        )
        {
            builder.AppendLine(DecodeQuotedPrintable(body.GetString() ?? string.Empty));
        }

        if (
            message.TryGetProperty("MIME", out var mime)
            && mime.TryGetProperty("Parts", out var parts)
        )
        {
            foreach (var part in parts.EnumerateArray())
            {
                if (part.TryGetProperty("Body", out var partBody))
                {
                    builder.AppendLine(DecodeQuotedPrintable(partBody.GetString() ?? string.Empty));
                }
            }
        }

        return builder.ToString();
    }

    /// <summary>Undoes the quoted-printable encoding MailHog stores so URLs are reassembled.</summary>
    private static string DecodeQuotedPrintable(string input)
    {
        // Remove soft line breaks, then decode =XX escapes.
        var unfolded = input.Replace("=\r\n", string.Empty).Replace("=\n", string.Empty);
        return QuotedPrintableRegex()
            .Replace(unfolded, match => ((char)Convert.ToInt32(match.Value[1..], 16)).ToString());
    }

    [GeneratedRegex(@"https?://[^\s""'<>]+")]
    private static partial Regex LinkRegex();

    [GeneratedRegex("=[0-9A-Fa-f]{2}")]
    private static partial Regex QuotedPrintableRegex();
}
