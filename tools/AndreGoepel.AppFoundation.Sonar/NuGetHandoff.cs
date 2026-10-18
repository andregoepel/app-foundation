using System.Text.Json;
using AndreGoepel.Core;

namespace AndreGoepel.AppFoundation.Sonar;

internal sealed record NuGetHandoffInput(
    NuGetRelease Release,
    string Directory,
    string ManifestSha256,
    string TagObjectSha
);

internal sealed record NuGetHandoffReceipt(PackageEvidence[] Packages)
{
    public bool DryRun { get; } = true;
    public bool ReleaseEligible { get; } = false;
}

internal interface INuGetHandoff
{
    Task<Result<NuGetHandoffReceipt>> VerifyAsync(NuGetHandoffInput input, CancellationToken ct);
}

internal sealed class NuGetHandoff(
    IReleaseReadiness readiness,
    IGitHubClient github,
    TimeProvider clock
) : INuGetHandoff
{
    public async Task<Result<NuGetHandoffReceipt>> VerifyAsync(
        NuGetHandoffInput input,
        CancellationToken ct
    )
    {
        var tag = await CheckTagAsync(input, ct);
        if (tag.IsFailure)
            return Result.Fail<NuGetHandoffReceipt>(tag.Error);

        var initial = await readiness.VerifyAsync(input.Release, ct);
        if (initial.IsFailure)
            return Result.Fail<NuGetHandoffReceipt>(initial.Error);

        var manifest = await ReadManifestAsync(input, initial.Value!, ct);
        if (manifest.IsFailure)
            return Result.Fail<NuGetHandoffReceipt>(manifest.Error);

        var packages = manifest.Value!.Packages;
        var files = await CheckFilesAsync(input.Directory, packages, ct);
        if (files.IsFailure)
            return Result.Fail<NuGetHandoffReceipt>(files.Error);

        var final = await readiness.VerifyAsync(input.Release, ct);
        tag = await CheckTagAsync(input, ct);
        if (
            final.IsFailure
            || tag.IsFailure
            || clock.GetUtcNow() >= manifest.Value.ExpiresAt
            || JsonSerializer.Serialize(initial.Value, JsonFormat.Options)
                != JsonSerializer.Serialize(final.Value, JsonFormat.Options)
        )
            return Result.Fail<NuGetHandoffReceipt>(
                "Handoff readiness or tag changed; trusted revalidation is required."
            );

        var finalManifest = await ReadManifestAsync(input, final.Value!, ct);
        if (finalManifest.IsFailure)
            return Result.Fail<NuGetHandoffReceipt>(finalManifest.Error);

        files = await CheckFilesAsync(input.Directory, packages, ct);
        if (files.IsFailure)
            return Result.Fail<NuGetHandoffReceipt>(files.Error);
        if (clock.GetUtcNow() >= manifest.Value.ExpiresAt)
            return Result.Fail<NuGetHandoffReceipt>(
                "Handoff evidence expired during transfer verification."
            );

        return Result.Ok(new NuGetHandoffReceipt(packages));
    }

    private Task<Result> CheckTagAsync(NuGetHandoffInput input, CancellationToken ct)
    {
        if (
            input.Release.Tag != "v" + input.Release.Version
            || !IsHex(input.TagObjectSha, 40)
            || !IsHex(input.ManifestSha256, 64)
        )
            return Task.FromResult(
                Result.Fail("Handoff requires a pinned manifest and immutable release tag object.")
            );

        return NuGetTag.VerifyAsync(github, input.Release, input.TagObjectSha, ct);
    }

    private async Task<Result<NuGetDryRunReceipt>> ReadManifestAsync(
        NuGetHandoffInput input,
        ReadinessReceipt readinessReceipt,
        CancellationToken ct
    )
    {
        var directory = new DirectoryInfo(Path.GetFullPath(input.Directory));
        for (var parent = directory; parent is not null; parent = parent.Parent)
        {
            if ((parent.Attributes & FileAttributes.ReparsePoint) != 0)
                return Result.Fail<NuGetDryRunReceipt>(
                    "Handoff directory symlinks are not accepted."
                );
        }

        var path = Path.Combine(directory.FullName, "dry-run.json");
        var info = new FileInfo(path);
        if (
            !info.Exists
            || info.Length > 65536
            || (info.Attributes & FileAttributes.ReparsePoint) != 0
        )
            return Result.Fail<NuGetDryRunReceipt>(
                "The pinned handoff manifest is missing or invalid."
            );

        var bytes = await File.ReadAllBytesAsync(path, ct);
        if (SourceFiles.Hash(bytes) != input.ManifestSha256)
            return Result.Fail<NuGetDryRunReceipt>(
                "The transferred manifest does not match its trusted digest."
            );

        using var document = JsonDocument.Parse(bytes);
        var json = document.RootElement;
        var manifest = json.Deserialize<NuGetDryRunReceipt>(JsonFormat.Options);
        if (
            manifest is null
            || json.GetProperty("schemaVersion").GetInt32() != 1
            || !json.GetProperty("dryRun").GetBoolean()
            || json.GetProperty("releaseEligible").GetBoolean()
            || manifest.Sha != input.Release.Sha
            || manifest.Version != input.Release.Version
            || manifest.ReadinessRunId != input.Release.ReadinessRunId
            || manifest.ReadinessAttempt != readinessReceipt.Run.Attempt
            || manifest.ExpiresAt != readinessReceipt.ExpiresAt
            || manifest.ExpiresAt <= clock.GetUtcNow()
        )
            return Result.Fail<NuGetDryRunReceipt>(
                "The handoff manifest does not bind to current readiness."
            );

        var inventory = CheckInventory(input.Release, manifest.Packages);
        return inventory.IsSuccess
            ? Result.Ok(manifest)
            : Result.Fail<NuGetDryRunReceipt>(inventory.Error);
    }

    private static Result CheckInventory(NuGetRelease release, PackageEvidence[]? packages)
    {
        if (packages is null || packages.Length != NuGetRelease.Packages.Length * 2)
            return Result.Fail("Handoff requires all six packages and six symbol packages.");

        var expected = NuGetRelease
            .Packages.SelectMany(id =>
                new[] { $"{id}.{release.Version}.nupkg", $"{id}.{release.Version}.snupkg" }
            )
            .Order(StringComparer.Ordinal);
        if (
            packages.Any(p => p is null)
            || !packages.Select(p => p.File).Order(StringComparer.Ordinal).SequenceEqual(expected)
            || packages.Any(p =>
                !NuGetRelease.Packages.Contains(p.PackageId, StringComparer.Ordinal)
                || p.Version != release.Version
                || p.Feed != NuGetRelease.Feed
                || p.File != $"{p.PackageId}.{p.Version}{Path.GetExtension(p.File)}"
                || !IsHex(p.Sha256, 64)
            )
        )
            return Result.Fail("The handoff package inventory, feed or hashes are invalid.");

        return Result.Ok();
    }

    private static async Task<Result> CheckFilesAsync(
        string directory,
        PackageEvidence[] packages,
        CancellationToken ct
    )
    {
        var path = Path.Combine(directory, "packages");
        if (
            !System.IO.Directory.Exists(path)
            || (File.GetAttributes(path) & FileAttributes.ReparsePoint) != 0
        )
            return Result.Fail("The transferred package directory is missing or linked.");

        var actual = System
            .IO.Directory.GetFiles(path)
            .Select(Path.GetFileName)
            .Order(StringComparer.Ordinal);
        if (
            !actual.SequenceEqual(packages.Select(p => p.File).Order(StringComparer.Ordinal))
            || System.IO.Directory.GetDirectories(path).Length != 0
        )
            return Result.Fail("Transferred packages do not exactly match the manifest.");

        foreach (var package in packages)
        {
            var file = Path.Combine(path, package.File);
            if (
                (File.GetAttributes(file) & FileAttributes.ReparsePoint) != 0
                || SourceFiles.Hash(await File.ReadAllBytesAsync(file, ct)) != package.Sha256
            )
                return Result.Fail("Transferred package or symbol bytes changed.");
        }

        return Result.Ok();
    }

    private static bool IsHex(string? value, int length) =>
        value is not null
        && value.Length == length
        && value.All(c => c is >= '0' and <= '9' or >= 'a' and <= 'f');
}
