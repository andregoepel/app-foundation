using System.IO.Compression;
using System.Text;
using AndreGoepel.AppFoundation.Sonar;
using Xunit;

namespace AndreGoepel.AppFoundation.Sonar.Tests;

public sealed class NuGetPackagesTests
{
    [Theory]
    [InlineData("")]
    [InlineData("missing-symbols")]
    [InlineData("extra-package")]
    [InlineData("wrong-version")]
    [InlineData("wrong-commit")]
    [InlineData("wrong-dependency")]
    [InlineData("wrong-binary")]
    [InlineData("wrong-symbols")]
    [InlineData("duplicate-entry")]
    public async Task Inspect_AllSixPackages_BindsVersionSourceAndSymbols(string fault)
    {
        // Arrange
        using var fixture = new ReleaseFixture();
        await fixture.InitializeAsync(TestContext.Current.CancellationToken);
        var directory = Path.Combine(fixture.Root, "artifacts/packages");
        CreatePackages(fixture.Release, directory, fault);

        // Act
        var result = await NuGetPackages.InspectAsync(
            fixture.Release,
            directory,
            TestContext.Current.CancellationToken
        );

        // Assert
        if (fault.Length == 0)
        {
            Assert.True(result.IsSuccess, result.Error);
            Assert.Equal(12, result.Value!.Length);
            Assert.Equal(6, result.Value.Select(p => p.PackageId).Distinct().Count());
            Assert.All(result.Value, p => Assert.Equal(NuGetRelease.Feed, p.Feed));
        }
        else
            Assert.True(result.IsFailure);
    }

    internal static void CreatePackages(NuGetRelease release, string directory, string fault = "")
    {
        Directory.CreateDirectory(directory);
        foreach (var id in NuGetRelease.Packages)
        {
            var built = Path.Combine(release.Root, "src", id, "bin/Release/net10.0");
            Directory.CreateDirectory(built);
            File.WriteAllText(Path.Combine(built, id + ".dll"), "assembly");
            File.WriteAllText(Path.Combine(built, id + ".pdb"), "symbols");
            foreach (var extension in new[] { ".nupkg", ".snupkg" })
            {
                if (fault == "missing-symbols" && extension == ".snupkg")
                    continue;
                using var stream = File.Create(
                    Path.Combine(directory, $"{id}.{release.Version}{extension}")
                );
                using var archive = new ZipArchive(stream, ZipArchiveMode.Create);
                var version = fault == "wrong-version" ? "1.0.0" : release.Version;
                var sha = fault == "wrong-commit" ? "wrong" : release.Sha;
                var dependency = fault == "wrong-dependency" ? "1.0.0" : release.Version;
                WriteEntry(
                    archive,
                    id + ".nuspec",
                    $"""
                    <package xmlns="http://schemas.microsoft.com/packaging/2013/05/nuspec.xsd"><metadata>
                    <id>{id}</id><version>{version}</version>
                    <repository url="https://github.com/andregoepel/app-foundation" commit="{sha}" />
                    <dependencies><group><dependency id="AndreGoepel.AppFoundation.Core" version="{dependency}" /></group></dependencies>
                    </metadata></package>
                    """
                );
                var binary = extension == ".nupkg" ? ".dll" : ".pdb";
                var content = binary == ".dll" ? "assembly" : "symbols";
                if (
                    (binary == ".dll" && fault == "wrong-binary")
                    || (binary == ".pdb" && fault == "wrong-symbols")
                )
                    content = "tampered";
                WriteEntry(archive, $"lib/net10.0/{id}{binary}", content);
                if (fault == "duplicate-entry")
                    WriteEntry(archive, $"lib/net10.0/{id}{binary}", content);
            }
        }
        if (fault == "extra-package")
            File.WriteAllText(Path.Combine(directory, "other.nupkg"), "unexpected");
    }

    private static void WriteEntry(ZipArchive archive, string name, string content)
    {
        using var writer = new StreamWriter(
            archive.CreateEntry(name).Open(),
            new UTF8Encoding(false)
        );
        writer.Write(content);
    }
}
