using System.IO.Compression;
using System.Net;
using System.Net.Http.Headers;
using System.Text.Json;

namespace AndreGoepel.AppFoundation.Sonar;

internal interface IGitHubArtifacts
{
    Task<byte[]> ReadAsync(JsonElement artifact, string filename, CancellationToken ct);
}

internal sealed class GitHubArtifacts(HttpClient client, string token) : IGitHubArtifacts
{
    internal const int MaximumBytes = 4 * 1024 * 1024;

    public async Task<byte[]> ReadAsync(JsonElement artifact, string filename, CancellationToken ct)
    {
        var id = artifact.GetProperty("id").GetInt64();
        using var request = new HttpRequestMessage(
            HttpMethod.Get,
            $"https://api.github.com/repos/{MainPolicy.Repository}/actions/artifacts/{id}/zip"
        );
        request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", token);
        request.Headers.UserAgent.ParseAdd("app-foundation-sonar/1.0");
        request.Headers.Add("X-GitHub-Api-Version", "2022-11-28");
        using var response = await client.SendAsync(
            request,
            HttpCompletionOption.ResponseHeadersRead,
            ct
        );
        var location = response.Headers.Location;
        if (
            response.StatusCode != HttpStatusCode.Redirect
            || location is null
            || !location.IsAbsoluteUri
            || location.Scheme != "https"
            || location.Port != 443
            || location.UserInfo.Length != 0
            || location.Fragment.Length != 0
            || !(
                location.Host.EndsWith(".blob.core.windows.net", StringComparison.Ordinal)
                || location.Host.EndsWith(
                    ".actions.githubusercontent.com",
                    StringComparison.Ordinal
                )
            )
        )
            throw new InvalidDataException(
                "GitHub did not return an approved artifact download location."
            );

        // Signed storage URLs receive no GitHub Authorization header.
        using var download = new HttpRequestMessage(HttpMethod.Get, location);
        using var archiveResponse = await client.SendAsync(
            download,
            HttpCompletionOption.ResponseHeadersRead,
            ct
        );
        archiveResponse.EnsureSuccessStatusCode();
        await using var archiveStream = await archiveResponse.Content.ReadAsStreamAsync(ct);
        var bytes = await ReadBoundedAsync(archiveStream, ct);
        if (artifact.GetProperty("digest").GetString() != "sha256:" + SourceFiles.Hash(bytes))
            throw new InvalidDataException("The artifact archive digest did not verify.");

        return await ReadEntryAsync(bytes, filename, ct);
    }

    internal static async Task<byte[]> ReadEntryAsync(
        byte[] bytes,
        string filename,
        CancellationToken ct
    )
    {
        using var input = new MemoryStream(bytes, writable: false);
        using var zip = new ZipArchive(input, ZipArchiveMode.Read);
        if (zip.Entries.Count != 1 || zip.Entries[0].FullName != filename)
            throw new InvalidDataException(
                "Unexpected or duplicate files in the readiness artifact."
            );

        await using var stream = await zip.Entries[0].OpenAsync(ct);
        return await ReadBoundedAsync(stream, ct);
    }

    private static async Task<byte[]> ReadBoundedAsync(Stream stream, CancellationToken ct)
    {
        using var output = new MemoryStream();
        var buffer = new byte[8192];
        int count;
        while ((count = await stream.ReadAsync(buffer, ct)) != 0)
        {
            if (output.Length + count > MaximumBytes)
                throw new InvalidDataException("Readiness artifact exceeds its size limit.");
            await output.WriteAsync(buffer.AsMemory(0, count), ct);
        }

        return output.ToArray();
    }
}
