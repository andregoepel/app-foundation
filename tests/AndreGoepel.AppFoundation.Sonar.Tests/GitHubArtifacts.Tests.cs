using System.IO.Compression;
using System.Net;
using System.Text;
using System.Text.Json;
using AndreGoepel.AppFoundation.Sonar;
using Xunit;

namespace AndreGoepel.AppFoundation.Sonar.Tests;

public sealed class GitHubArtifactsTests
{
    [Fact]
    public async Task Read_AuthenticatedRedirect_DoesNotForwardCredentialsAndVerifiesDigest()
    {
        // Arrange
        var bytes = Archive("readiness.json", "{\"verified\":true}");
        using var handler = new ArtifactHandler(bytes);
        using var client = new HttpClient(handler);
        var artifacts = new GitHubArtifacts(client, "read-token");

        // Act
        var result = await artifacts.ReadAsync(
            Metadata(bytes),
            "readiness.json",
            TestContext.Current.CancellationToken
        );

        // Assert
        Assert.Equal("{\"verified\":true}", Encoding.UTF8.GetString(result));
        Assert.Equal(["Bearer read-token", null], handler.Authorization);
        Assert.Equal(2, handler.Requests);
    }

    [Theory]
    [InlineData("http://store.blob.core.windows.net/archive")]
    [InlineData("https://evil.example/archive")]
    [InlineData("https://store.blob.core.windows.net.evil.example/archive")]
    [InlineData("https://user@store.blob.core.windows.net/archive")]
    [InlineData("https://store.blob.core.windows.net:444/archive")]
    public async Task Read_UnapprovedRedirect_StopsBeforeStorageRequest(string location)
    {
        // Arrange
        var bytes = Archive("readiness.json", "{}");
        using var handler = new ArtifactHandler(bytes) { Location = location };
        using var client = new HttpClient(handler);

        // Act / Assert
        await Assert.ThrowsAsync<InvalidDataException>(() =>
            new GitHubArtifacts(client, "read-token").ReadAsync(
                Metadata(bytes),
                "readiness.json",
                TestContext.Current.CancellationToken
            )
        );
        Assert.Equal(1, handler.Requests);
    }

    [Fact]
    public async Task Read_TamperedArchive_RejectsDigest()
    {
        // Arrange
        var bytes = Archive("readiness.json", "{}");
        using var handler = new ArtifactHandler(bytes);
        using var client = new HttpClient(handler);

        // Act / Assert
        await Assert.ThrowsAsync<InvalidDataException>(() =>
            new GitHubArtifacts(client, "read-token").ReadAsync(
                Metadata([0]),
                "readiness.json",
                TestContext.Current.CancellationToken
            )
        );
    }

    [Theory]
    [InlineData("../readiness.json", 1)]
    [InlineData("summary.json", 1)]
    [InlineData("readiness.json", 2)]
    [InlineData("readiness.json", 0)]
    public async Task ReadEntry_UnexpectedZipEntries_RejectsWithoutExtraction(
        string filename,
        int copies
    )
    {
        // Arrange
        var bytes = Archive(filename, "{}", copies);

        // Act / Assert
        await Assert.ThrowsAsync<InvalidDataException>(() =>
            GitHubArtifacts.ReadEntryAsync(
                bytes,
                "readiness.json",
                TestContext.Current.CancellationToken
            )
        );
    }

    [Fact]
    public async Task ReadEntry_CompressedOversizedDocument_StopsAtLimit()
    {
        // Arrange
        var bytes = Archive("readiness.json", new string('x', GitHubArtifacts.MaximumBytes + 1));

        // Act / Assert
        await Assert.ThrowsAsync<InvalidDataException>(() =>
            GitHubArtifacts.ReadEntryAsync(
                bytes,
                "readiness.json",
                TestContext.Current.CancellationToken
            )
        );
    }

    [Theory]
    [InlineData(HttpStatusCode.Unauthorized)]
    [InlineData(HttpStatusCode.Forbidden)]
    [InlineData(HttpStatusCode.ServiceUnavailable)]
    public async Task Read_ApiFailure_Blocks(HttpStatusCode status)
    {
        // Arrange
        var bytes = Archive("readiness.json", "{}");
        using var handler = new ArtifactHandler(bytes) { Status = status };
        using var client = new HttpClient(handler);

        // Act / Assert
        await Assert.ThrowsAsync<InvalidDataException>(() =>
            new GitHubArtifacts(client, "read-token").ReadAsync(
                Metadata(bytes),
                "readiness.json",
                TestContext.Current.CancellationToken
            )
        );
        Assert.Equal(1, handler.Requests);
    }

    private static JsonElement Metadata(byte[] bytes) =>
        JsonSerializer.SerializeToElement(
            new { id = 123, digest = "sha256:" + SourceFiles.Hash(bytes) }
        );

    private static byte[] Archive(string filename, string content, int copies = 1)
    {
        using var stream = new MemoryStream();
        using (var archive = new ZipArchive(stream, ZipArchiveMode.Create, leaveOpen: true))
        {
            for (var index = 0; index < copies; index++)
            {
                using var writer = new StreamWriter(
                    archive.CreateEntry(filename).Open(),
                    new UTF8Encoding(false)
                );
                writer.Write(content);
            }
        }
        return stream.ToArray();
    }

    private sealed class ArtifactHandler(byte[] bytes) : HttpMessageHandler
    {
        internal List<string?> Authorization { get; } = [];
        internal int Requests { get; private set; }
        internal string Location { get; init; } =
            "https://store.blob.core.windows.net/archive?signature=private";
        internal HttpStatusCode Status { get; init; } = HttpStatusCode.Redirect;

        protected override Task<HttpResponseMessage> SendAsync(
            HttpRequestMessage request,
            CancellationToken cancellationToken
        )
        {
            Requests++;
            Authorization.Add(request.Headers.Authorization?.ToString());
            return Task.FromResult(
                Requests == 1
                    ? new HttpResponseMessage(Status) { Headers = { Location = new Uri(Location) } }
                    : new HttpResponseMessage(HttpStatusCode.OK)
                    {
                        Content = new ByteArrayContent(bytes),
                    }
            );
        }
    }
}
