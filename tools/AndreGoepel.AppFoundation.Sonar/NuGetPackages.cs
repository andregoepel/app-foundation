using System.IO.Compression;
using System.Xml.Linq;
using AndreGoepel.Core;

namespace AndreGoepel.AppFoundation.Sonar;

internal static class NuGetPackages
{
    internal static async Task<Result<PackageEvidence[]>> InspectAsync(
        NuGetRelease release,
        string directory,
        CancellationToken ct
    )
    {
        var expected = NuGetRelease
            .Packages.SelectMany(id =>
                new[] { $"{id}.{release.Version}.nupkg", $"{id}.{release.Version}.snupkg" }
            )
            .Order(StringComparer.Ordinal)
            .ToArray();
        var actual = Directory
            .GetFiles(directory)
            .Select(Path.GetFileName)
            .Order(StringComparer.Ordinal);
        if (!actual.SequenceEqual(expected) || Directory.GetDirectories(directory).Length != 0)
            return Result.Fail<PackageEvidence[]>(
                "Expected exactly six packages and six symbol packages."
            );

        var evidence = new List<PackageEvidence>();
        foreach (var filename in expected)
        {
            var path = Path.Combine(directory, filename);
            if ((File.GetAttributes(path) & FileAttributes.ReparsePoint) != 0)
                return Result.Fail<PackageEvidence[]>("Package symlinks are not accepted.");

            await using var stream = File.OpenRead(path);
            using var archive = new ZipArchive(stream, ZipArchiveMode.Read);
            var metadata = archive
                .Entries.Where(e => e.FullName.EndsWith(".nuspec", StringComparison.Ordinal))
                .ToArray();
            if (
                metadata.Length != 1
                || archive.Entries.Select(e => e.FullName).Distinct(StringComparer.Ordinal).Count()
                    != archive.Entries.Count
            )
                return Result.Fail<PackageEvidence[]>(
                    "Invalid package metadata or duplicate archive entries."
                );

            await using var xml = await metadata[0].OpenAsync(ct);
            var document = await XDocument.LoadAsync(xml, LoadOptions.None, ct);
            var ns = document.Root!.Name.Namespace;
            var package = document.Root.Element(ns + "metadata")!;
            var id = package.Element(ns + "id")?.Value;
            var repository = package.Element(ns + "repository");
            if (
                id is null
                || !NuGetRelease.Packages.Contains(id, StringComparer.Ordinal)
                || package.Element(ns + "version")?.Value != release.Version
                || filename != $"{id}.{release.Version}{Path.GetExtension(filename)}"
                || (string?)repository?.Attribute("commit") != release.Sha
                || (string?)repository?.Attribute("url")
                    != "https://github.com/" + MainPolicy.Repository
            )
                return Result.Fail<PackageEvidence[]>(
                    "Unexpected package ID, version or repository commit."
                );

            if (
                package
                    .Descendants(ns + "dependency")
                    .Any(d =>
                        NuGetRelease.Packages.Contains(
                            (string?)d.Attribute("id"),
                            StringComparer.Ordinal
                        )
                        && (string?)d.Attribute("version") != release.Version
                    )
            )
                return Result.Fail<PackageEvidence[]>(
                    "Internal package dependencies do not use the release version."
                );

            var extension = filename.EndsWith(".snupkg", StringComparison.Ordinal)
                ? ".pdb"
                : ".dll";
            var binary = archive.GetEntry($"lib/net10.0/{id}{extension}");
            var built = Path.Combine(
                release.Root,
                "src",
                id,
                "bin/Release/net10.0",
                id + extension
            );
            if (binary is null || !File.Exists(built))
                return Result.Fail<PackageEvidence[]>(
                    "A package or its matching built binary is missing."
                );

            await using var payload = await binary.OpenAsync(ct);
            using var buffer = new MemoryStream();
            await payload.CopyToAsync(buffer, ct);
            if (
                SourceFiles.Hash(buffer.ToArray())
                != SourceFiles.Hash(await File.ReadAllBytesAsync(built, ct))
            )
                return Result.Fail<PackageEvidence[]>(
                    "Package content differs from the tested versioned build."
                );

            evidence.Add(
                new PackageEvidence(
                    filename,
                    id,
                    release.Version,
                    SourceFiles.Hash(await File.ReadAllBytesAsync(path, ct)),
                    NuGetRelease.Feed
                )
            );
        }

        return Result.Ok(evidence.ToArray());
    }
}
