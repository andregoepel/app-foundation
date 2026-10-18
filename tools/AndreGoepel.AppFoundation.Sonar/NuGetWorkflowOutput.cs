using System.Text;
using System.Text.Json;
using AndreGoepel.Core;

namespace AndreGoepel.AppFoundation.Sonar;

internal static class NuGetWorkflowOutput
{
    internal static async Task<Result> CompleteAsync(
        Result<NuGetPreparationOutput> prepared,
        NuGetPreparationContext context,
        Func<string, string?> environment,
        CancellationToken ct
    )
    {
        if (prepared.IsFailure)
            return prepared;

        var output = prepared.Value!;
        var path = environment("GITHUB_OUTPUT");
        if (!string.IsNullOrEmpty(path))
        {
            var exported = await ExportAsync(output, context, path, environment("RUNNER_TEMP"), ct);
            if (exported.IsFailure)
                return exported;
        }

        ct.ThrowIfCancellationRequested();
        Console.WriteLine(JsonSerializer.Serialize(output, JsonFormat.Options));
        return Result.Ok();
    }

    private static async Task<Result> ExportAsync(
        NuGetPreparationOutput output,
        NuGetPreparationContext context,
        string path,
        string? temporary,
        CancellationToken ct
    )
    {
        if (
            string.IsNullOrEmpty(temporary)
            || !Path.IsPathFullyQualified(temporary)
            || !Path.IsPathFullyQualified(path)
            || !IsWithin(temporary, path)
            || !File.Exists(path)
            || HasLink(path)
        )
            return Result.Fail(
                "The runner output file is missing or outside its temporary directory."
            );

        var batch = Path.GetDirectoryName(output.ManifestPath);
        var artifact = $"{context.Release.Sha}-{context.RunId}-{context.Attempt}";
        if (
            batch is null
            || !Path.IsPathFullyQualified(output.ManifestPath)
            || !IsWithin(Path.Combine(context.Release.Root, "artifacts/sonar"), output.ManifestPath)
            || Path.GetFileName(output.ManifestPath) != "dry-run.json"
            || output.OriginPath != Path.Combine(batch, "origin", "origin.json")
            || output.ArtifactName != "nuget-origin-" + artifact
            || !File.Exists(output.ManifestPath)
            || !File.Exists(output.OriginPath)
            || !Directory.Exists(Path.Combine(batch, "packages"))
            || HasLink(output.ManifestPath)
            || HasLink(output.OriginPath)
            || HasLink(Path.Combine(batch, "packages"))
        )
            return Result.Fail("Workflow outputs must identify the verified preparation batch.");

        string[] lines =
        [
            "manifest-path=" + output.ManifestPath,
            "packages-path=" + Path.Combine(batch, "packages"),
            "origin-path=" + output.OriginPath,
            "origin-artifact-name=nuget-origin-" + artifact,
            "batch-artifact-name=nuget-batch-" + artifact,
        ];
        if (lines.Any(line => line.IndexOfAny(['\r', '\n', '\0', '*', '?', '[', ']', '!']) >= 0))
            return Result.Fail(
                "Workflow output values cannot contain line breaks or artifact glob syntax."
            );

        await using var stream = new FileStream(
            path,
            FileMode.Open,
            FileAccess.Write,
            FileShare.None,
            4096,
            useAsync: true
        );
        if (stream.Length != 0)
            return Result.Fail("Preparation requires an empty runner output file.");

        await stream.WriteAsync(Encoding.UTF8.GetBytes(string.Join('\n', lines) + "\n"), ct);
        return Result.Ok();
    }

    private static bool IsWithin(string directory, string path)
    {
        var relative = Path.GetRelativePath(Path.GetFullPath(directory), Path.GetFullPath(path));
        return !Path.IsPathRooted(relative)
            && relative != ".."
            && !relative.StartsWith(".." + Path.DirectorySeparatorChar, StringComparison.Ordinal);
    }

    private static bool HasLink(string path)
    {
        for (
            FileSystemInfo? item = new FileInfo(path);
            item is not null;
            item = item is FileInfo file ? file.Directory : ((DirectoryInfo)item).Parent
        )
            if ((item.Attributes & FileAttributes.ReparsePoint) != 0)
                return true;
        return false;
    }
}
