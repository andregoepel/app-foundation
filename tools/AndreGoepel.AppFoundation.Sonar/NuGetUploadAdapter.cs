using System.Net;
using System.Security.Cryptography;
using AndreGoepel.Core;
using NuGet.Configuration;
using NuGet.Protocol;
using NuGet.Protocol.Core.Types;
using NullLogger = NuGet.Common.NullLogger;

namespace AndreGoepel.AppFoundation.Sonar;

internal enum NuGetUploadOutcome
{
    Accepted,
    Unknown,
}

internal sealed record NuGetUploadAttempt(PackageEvidence Package, NuGetUploadOutcome Outcome)
{
    public bool RequiresReconciliation => Outcome == NuGetUploadOutcome.Unknown;
}

internal sealed class NuGetUploadAdapter(
    TimeProvider clock,
    Func<HttpMessageHandler> transportFactory
)
{
    internal const string PackageEndpoint = "https://www.nuget.org/api/v2/package/";
    internal const string SymbolEndpoint = "https://www.nuget.org/api/v2/symbolpackage/";
    internal const long MaximumPackageBytes = 250L * 1024 * 1024;

    internal NuGetUploadAdapter(TimeProvider clock)
        : this(clock, CreateTransport) { }

    internal static HttpClientHandler CreateTransport() =>
        new()
        {
            AllowAutoRedirect = false,
            UseCookies = false,
            UseDefaultCredentials = false,
            UseProxy = false,
            AutomaticDecompression = DecompressionMethods.None,
        };

    internal async Task<Result<NuGetUploadAttempt>> SendAsync(
        PackageEvidence package,
        Stream bytes,
        NuGetUploadCredential credential,
        CancellationToken ct
    )
    {
        ct.ThrowIfCancellationRequested();
        if (!IsValid(package) || !bytes.CanRead || !credential.IsUsable(clock.GetUtcNow()))
            return Result.Fail<NuGetUploadAttempt>(
                "Upload requires an approved file identity and an unexpired temporary key."
            );

        var directory = Directory.CreateTempSubdirectory("appfoundation-nuget-upload-");
        var path = Path.Combine(directory.FullName, package.File);
        var endpoint = package.File.EndsWith(".snupkg", StringComparison.Ordinal)
            ? SymbolEndpoint
            : PackageEndpoint;
        using var transport = new NuGetUploadTransport(new Uri(endpoint), credential, clock, ct);
        try
        {
            await using (
                var staging = new FileStream(
                    path,
                    FileMode.CreateNew,
                    FileAccess.Write,
                    FileShare.None,
                    81920,
                    useAsync: true
                )
            )
            {
                if (!await CopySnapshotAsync(bytes, staging, package.Sha256, ct))
                    return Result.Fail<NuGetUploadAttempt>(
                        "Upload bytes are empty, oversized or different from the verified snapshot."
                    );
            }

            // Keep a read-only handle open while NuGet reads the verified file by path.
            await using var snapshot = new FileStream(
                path,
                FileMode.Open,
                FileAccess.Read,
                FileShare.Read
            );
            if (
                snapshot.Length > MaximumPackageBytes
                || Convert.ToHexStringLower(await SHA256.HashDataAsync(snapshot, ct))
                    != package.Sha256
            )
                return Result.Fail<NuGetUploadAttempt>("The staged upload snapshot changed.");
            if (!credential.IsUsable(clock.GetUtcNow()))
                return Result.Fail<NuGetUploadAttempt>("The temporary upload key expired.");

            transport.InnerHandler = transportFactory();
            using var settingsHandler = CreateTransport();
            using var source = new HttpSource(
                new PackageSource(endpoint),
                () =>
                    Task.FromResult<HttpHandlerResource>(
                        new HttpHandlerResourceV3(settingsHandler, transport)
                    ),
                NullThrottle.Instance
            );
            var client = new PackageUpdateResource(endpoint, source)
            {
                Settings = NullSettings.Instance,
            };
            await client.PushAsync(
                [path],
                symbolSource: endpoint == SymbolEndpoint ? endpoint : "",
                timeoutInSecond: 300,
                disableBuffering: false,
                getApiKey: _ => credential.Key,
                getSymbolApiKey: _ => credential.Key,
                noServiceEndpoint: true,
                skipDuplicate: false,
                allowSnupkg: endpoint == SymbolEndpoint,
                allowInsecureConnections: false,
                log: NullLogger.Instance
            );

            return transport.Started
                ? Result.Ok(
                    new NuGetUploadAttempt(
                        package,
                        transport.Acknowledged
                            ? NuGetUploadOutcome.Accepted
                            : NuGetUploadOutcome.Unknown
                    )
                )
                : Result.Fail<NuGetUploadAttempt>(
                    "The NuGet client did not send the verified file."
                );
        }
        catch (Exception error)
            when (error
                    is HttpRequestException
                        or IOException
                        or UnauthorizedAccessException
                        or InvalidOperationException
                        or OperationCanceledException
                        or ArgumentException
            )
        {
            return transport.Started
                ? Result.Ok(new NuGetUploadAttempt(package, NuGetUploadOutcome.Unknown))
                : Result.Fail<NuGetUploadAttempt>("The verified file could not be submitted.");
        }
        finally
        {
            File.Delete(path);
            directory.Delete();
        }
    }

    private static bool IsValid(PackageEvidence package) =>
        package.Feed == NuGetRelease.Feed
        && NuGetRelease.Packages.Contains(package.PackageId, StringComparer.Ordinal)
        && NuGetRelease.IsPreparationTag("v" + package.Version, package.Version)
        && package.Sha256.Length == 64
        && package.Sha256.All(c => c is >= '0' and <= '9' or >= 'a' and <= 'f')
        && (
            package.File == $"{package.PackageId}.{package.Version}.nupkg"
            || package.File == $"{package.PackageId}.{package.Version}.snupkg"
        );

    private static async Task<bool> CopySnapshotAsync(
        Stream input,
        Stream output,
        string expectedHash,
        CancellationToken ct
    )
    {
        if (input.CanSeek && input.Length - input.Position > MaximumPackageBytes)
            return false;

        using var hash = IncrementalHash.CreateHash(HashAlgorithmName.SHA256);
        var buffer = new byte[81920];
        long length = 0;
        int count;
        while ((count = await input.ReadAsync(buffer, ct)) != 0)
        {
            length += count;
            if (length > MaximumPackageBytes)
                return false;

            hash.AppendData(buffer, 0, count);
            await output.WriteAsync(buffer.AsMemory(0, count), ct);
        }
        return length > 0 && Convert.ToHexStringLower(hash.GetHashAndReset()) == expectedHash;
    }
}
