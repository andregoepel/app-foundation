using System.Security.Cryptography;
using System.Text;
using System.Text.Json;

namespace AndreGoepel.AppFoundation.Sonar;

internal static class SourceFiles
{
    private static readonly HashSet<string> Generated = new(StringComparer.Ordinal)
    {
        "bin",
        "obj",
        ".git",
        ".sonarqube",
        ".sonar-analysis.lock",
        "artifacts",
        "__pycache__",
        "node_modules",
    };

    internal static string Relative(string root, string path)
    {
        var relative = Path.GetRelativePath(root, Path.GetFullPath(path)).Replace('\\', '/');
        if (
            relative == ".."
            || relative.StartsWith("../", StringComparison.Ordinal)
            || Path.IsPathRooted(relative)
        )
            throw new InvalidDataException("A source path lies outside this checkout.");

        return relative;
    }

    internal static string Hash(byte[] bytes) => Convert.ToHexStringLower(SHA256.HashData(bytes));

    internal static async Task<string> SnapshotAsync(
        string root,
        CancellationToken cancellationToken
    )
    {
        var files = new SortedDictionary<string, string>(StringComparer.Ordinal);

        foreach (var file in Enumerate(new DirectoryInfo(root)))
            files[Relative(root, file.FullName)] = Hash(
                await File.ReadAllBytesAsync(file.FullName, cancellationToken)
            );

        return Hash(Encoding.UTF8.GetBytes(JsonSerializer.Serialize(files)));
    }

    private static IEnumerable<FileInfo> Enumerate(DirectoryInfo directory)
    {
        foreach (var entry in directory.EnumerateFileSystemInfos())
        {
            if (Generated.Contains(entry.Name))
                continue;

            if ((entry.Attributes & FileAttributes.ReparsePoint) != 0)
                throw new InvalidDataException(
                    "Source symlinks require an explicit mapping review."
                );

            if (entry is DirectoryInfo child)
            {
                foreach (var file in Enumerate(child))
                    yield return file;
            }
            else if (entry is FileInfo file)
                yield return file;
        }
    }
}
