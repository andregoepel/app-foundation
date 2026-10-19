using System.IO.Compression;
using System.Net;
using System.Text.Json;
using AndreGoepel.AppFoundation.Sonar;
using Xunit;

namespace AndreGoepel.AppFoundation.Sonar.Tests;

public sealed class NuGetUploadAdapterTests
{
    [Theory]
    [InlineData(false, HttpStatusCode.Created)]
    [InlineData(false, HttpStatusCode.Accepted)]
    [InlineData(true, HttpStatusCode.Created)]
    [InlineData(true, HttpStatusCode.Accepted)]
    public async Task Send_ApprovedFile_UsesOfficialClientForExactlyOneFile(
        bool symbols,
        HttpStatusCode status
    )
    {
        // Arrange
        using var fixture = new UploadFixture(symbols) { Status = status };

        // Act
        var result = await fixture.SendAsync(TestContext.Current.CancellationToken);

        // Assert
        Assert.True(result.IsSuccess, result.Error);
        Assert.Equal(NuGetUploadOutcome.Accepted, result.Value!.Outcome);
        Assert.False(result.Value.RequiresReconciliation);
        var request = Assert.Single(fixture.Requests);
        Assert.Equal(HttpMethod.Put, request.Method);
        Assert.Equal(
            symbols ? NuGetUploadAdapter.SymbolEndpoint : NuGetUploadAdapter.PackageEndpoint,
            request.Endpoint
        );
        Assert.Equal(fixture.Package.Sha256, SourceFiles.Hash(request.Bytes));
        Assert.Equal(fixture.Bytes, request.Bytes);
        Assert.Equal(UploadFixture.Key, request.ApiKey);
        Assert.DoesNotContain(UploadFixture.Key, JsonSerializer.Serialize(result.Value));
    }

    [Theory]
    [InlineData("feed")]
    [InlineData("package")]
    [InlineData("version")]
    [InlineData("path")]
    [InlineData("extension")]
    [InlineData("hash")]
    [InlineData("uppercase-hash")]
    public async Task Send_InvalidFileIdentity_StopsBeforeCreatingTransport(string fault)
    {
        // Arrange
        using var fixture = new UploadFixture();
        fixture.Package = fault switch
        {
            "feed" => fixture.Package with { Feed = "https://other.example/v3/index.json" },
            "package" => fixture.Package with { PackageId = "Unapproved.Package" },
            "version" => fixture.Package with { Version = "2.2.0-beta.1" },
            "path" => fixture.Package with { File = "../" + fixture.Package.File },
            "extension" => fixture.Package with { File = "AndreGoepel.AppFoundation.2.2.0.zip" },
            "hash" => fixture.Package with { Sha256 = "missing" },
            _ => fixture.Package with { Sha256 = new string('A', 64) },
        };

        // Act
        var result = await fixture.SendAsync(TestContext.Current.CancellationToken);

        // Assert
        Assert.True(result.IsFailure, fault);
        Assert.Equal(0, fixture.Transports);
        Assert.Empty(fixture.Requests);
    }

    [Theory]
    [InlineData("changed")]
    [InlineData("empty")]
    [InlineData("unreadable")]
    [InlineData("oversized")]
    public async Task Send_InvalidSnapshot_StopsBeforeCreatingTransport(string fault)
    {
        // Arrange
        using var fixture = new UploadFixture();
        using var input =
            fault == "oversized"
                ? new OversizedStream()
                : new MemoryStream(fault == "empty" ? [] : fixture.Bytes);
        if (fault == "changed")
            fixture.Package = fixture.Package with { Sha256 = new string('0', 64) };
        if (fault == "unreadable")
            input.Dispose();

        // Act
        var result = await fixture.Adapter.SendAsync(
            fixture.Package,
            input,
            fixture.Credential,
            TestContext.Current.CancellationToken
        );

        // Assert
        Assert.True(result.IsFailure, fault);
        Assert.Equal(0, fixture.Transports);
        Assert.Empty(fixture.Requests);
    }

    [Theory]
    [InlineData("empty")]
    [InlineData("control")]
    [InlineData("expired")]
    [InlineData("long-lived")]
    [InlineData("disposed")]
    public async Task Send_InvalidCredential_StopsBeforeTransportAndRedactsDiagnostics(string fault)
    {
        // Arrange
        using var fixture = new UploadFixture();
        using var credential = new NuGetUploadCredential(
            fault == "empty" ? " "
                : fault == "control" ? UploadFixture.Key + "\n"
                : UploadFixture.Key,
            fault == "expired"
                ? fixture.Clock.Now
                : fixture.Clock.Now.AddMinutes(fault == "long-lived" ? 61 : 30)
        );
        if (fault == "disposed")
            credential.Dispose();
        using var input = new MemoryStream(fixture.Bytes);

        // Act
        var result = await fixture.Adapter.SendAsync(
            fixture.Package,
            input,
            credential,
            TestContext.Current.CancellationToken
        );

        // Assert
        Assert.True(result.IsFailure, fault);
        Assert.Empty(fixture.Requests);
        Assert.DoesNotContain(UploadFixture.Key, result.Error);
        Assert.DoesNotContain(UploadFixture.Key, credential.ToString());
        Assert.DoesNotContain(UploadFixture.Key, JsonSerializer.Serialize(credential));
    }

    [Theory]
    [InlineData(HttpStatusCode.Unauthorized)]
    [InlineData(HttpStatusCode.Forbidden)]
    [InlineData(HttpStatusCode.Conflict)]
    [InlineData(HttpStatusCode.TooManyRequests)]
    [InlineData(HttpStatusCode.InternalServerError)]
    [InlineData(HttpStatusCode.TemporaryRedirect)]
    [InlineData(HttpStatusCode.OK)]
    [InlineData(HttpStatusCode.NoContent)]
    public async Task Send_UnacknowledgedResponse_RequiresReconciliationWithoutSecondRequest(
        HttpStatusCode status
    )
    {
        // Arrange
        using var fixture = new UploadFixture { Status = status };

        // Act
        var result = await fixture.SendAsync(TestContext.Current.CancellationToken);

        // Assert
        Assert.True(result.IsSuccess, result.Error);
        Assert.Equal(NuGetUploadOutcome.Unknown, result.Value!.Outcome);
        Assert.True(result.Value.RequiresReconciliation);
        Assert.Single(fixture.Requests);
        Assert.DoesNotContain(UploadFixture.Key, JsonSerializer.Serialize(result.Value));
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task Send_ResponseLostOrCancelledAfterSending_RecordsUnknownWithoutRetry(
        bool cancelled
    )
    {
        // Arrange
        using var fixture = new UploadFixture();
        fixture.Failure = cancelled
            ? new OperationCanceledException(UploadFixture.Key)
            : new HttpRequestException(UploadFixture.Key);

        // Act
        var result = await fixture.SendAsync(TestContext.Current.CancellationToken);

        // Assert
        Assert.True(result.IsSuccess, result.Error);
        Assert.Equal(NuGetUploadOutcome.Unknown, result.Value!.Outcome);
        Assert.Single(fixture.Requests);
        Assert.DoesNotContain(UploadFixture.Key, JsonSerializer.Serialize(result.Value));
    }

    [Fact]
    public async Task Send_CancelledBeforeSubmission_DoesNotCreateTransport()
    {
        // Arrange
        using var fixture = new UploadFixture();
        using var cancellation = new CancellationTokenSource();
        await cancellation.CancelAsync();

        // Act
        var action = () => fixture.SendAsync(cancellation.Token);

        // Assert
        await Assert.ThrowsAnyAsync<OperationCanceledException>(action);
        Assert.Equal(0, fixture.Transports);
        Assert.Empty(fixture.Requests);
    }

    [Fact]
    public async Task Send_CredentialExpiresBeforeHttpRequest_DoesNotSend()
    {
        // Arrange
        using var fixture = new UploadFixture();
        fixture.OnCreateTransport = () => fixture.Clock.Now = fixture.Clock.Now.AddHours(1);

        // Act
        var result = await fixture.SendAsync(TestContext.Current.CancellationToken);

        // Assert
        Assert.True(result.IsFailure);
        Assert.Empty(fixture.Requests);
    }

    [Fact]
    public void CreateTransport_ProductionDefaults_DisablesRedirectAndAmbientAuthentication()
    {
        // Act
        using var transport = NuGetUploadAdapter.CreateTransport();

        // Assert
        Assert.False(transport.AllowAutoRedirect);
        Assert.False(transport.UseCookies);
        Assert.False(transport.UseDefaultCredentials);
        Assert.False(transport.UseProxy);
        Assert.Null(transport.Credentials);
        Assert.Equal(DecompressionMethods.None, transport.AutomaticDecompression);
    }

    internal sealed class UploadClock : TimeProvider
    {
        internal DateTimeOffset Now { get; set; } = DateTimeOffset.UtcNow;

        public override DateTimeOffset GetUtcNow() => Now;
    }

    internal sealed record ObservedUpload(
        HttpMethod Method,
        string Endpoint,
        byte[] Bytes,
        string ApiKey
    );

    internal sealed class UploadFixture : HttpMessageHandler
    {
        internal const string Key = "synthetic-upload-key-never-use-for-publication";
        internal UploadClock Clock { get; } = new();
        internal NuGetUploadCredential Credential { get; }
        internal PackageEvidence Package { get; set; }
        internal byte[] Bytes { get; }
        internal NuGetUploadAdapter Adapter { get; }
        internal int Transports { get; private set; }
        internal List<ObservedUpload> Requests { get; } = [];
        internal HttpStatusCode Status { get; set; } = HttpStatusCode.Created;
        internal Exception? Failure { get; set; }
        internal Action? OnCreateTransport { get; set; }

        internal UploadFixture(bool symbols = false)
        {
            using var buffer = new MemoryStream();
            using (var archive = new ZipArchive(buffer, ZipArchiveMode.Create, leaveOpen: true))
            {
                using var writer = new StreamWriter(
                    archive.CreateEntry("AndreGoepel.AppFoundation.nuspec").Open()
                );
                writer.Write(
                    "<package><metadata><id>AndreGoepel.AppFoundation</id><version>2.2.0</version><authors>fixture</authors><description>Offline upload fixture</description></metadata></package>"
                );
            }
            Bytes = buffer.ToArray();
            Package = new PackageEvidence(
                "AndreGoepel.AppFoundation.2.2.0." + (symbols ? "snupkg" : "nupkg"),
                "AndreGoepel.AppFoundation",
                "2.2.0",
                SourceFiles.Hash(Bytes),
                NuGetRelease.Feed
            );
            Credential = new NuGetUploadCredential(Key, Clock.Now.AddMinutes(30));
            Adapter = new NuGetUploadAdapter(
                Clock,
                () =>
                {
                    Transports++;
                    OnCreateTransport?.Invoke();
                    return this;
                }
            );
        }

        internal async Task<AndreGoepel.Core.Result<NuGetUploadAttempt>> SendAsync(
            CancellationToken ct
        )
        {
            using var bytes = new MemoryStream(Bytes, writable: false);
            return await Adapter.SendAsync(Package, bytes, Credential, ct);
        }

        protected override async Task<HttpResponseMessage> SendAsync(
            HttpRequestMessage request,
            CancellationToken cancellationToken
        )
        {
            var multipart = Assert.IsType<MultipartContent>(request.Content, exactMatch: false);
            var body = Assert.Single(multipart);
            Requests.Add(
                new ObservedUpload(
                    request.Method,
                    request.RequestUri!.AbsoluteUri,
                    await body.ReadAsByteArrayAsync(cancellationToken),
                    request.Headers.GetValues("X-NuGet-ApiKey").Single()
                )
            );
            if (Failure is not null)
                throw Failure;
            return new HttpResponseMessage(Status)
            {
                Content = new StringContent(Key),
                Headers = { Location = new Uri("https://untrusted.example/redirect") },
            };
        }
    }

    private sealed class OversizedStream : MemoryStream
    {
        public override long Length => NuGetUploadAdapter.MaximumPackageBytes + 1;
    }
}
