using System.Globalization;
using System.Text;
using System.Text.Json;
using System.Text.RegularExpressions;
using System.Xml.Linq;
using AndreGoepel.Core;

namespace AndreGoepel.AppFoundation.Sonar;

internal static partial class ReportValidation
{
    private static readonly string[] ProjectFolders = ["src", "samples", "tests", "tools"];
    private static readonly string[] ProductionPrefixes = ["src/", "samples/", "tools/"];

    internal static async Task<XDocument> ReadFreshAsync(
        string path,
        DateTimeOffset started,
        CancellationToken cancellationToken
    )
    {
        var file = new FileInfo(path);
        if (!file.Exists || file.Length == 0 || file.LastWriteTimeUtc < started.UtcDateTime)
            throw new InvalidDataException($"Missing, empty or stale report: {file.Name}");

        await using var stream = File.OpenRead(path);
        return await XDocument.LoadAsync(stream, LoadOptions.None, cancellationToken);
    }

    internal static Result<string[]> Discovery(string output, int minimum)
    {
        var names = output
            .Split('\n')
            .Where(line => line.StartsWith("  AndreGoepel.", StringComparison.Ordinal))
            .Select(line => line.Trim())
            .ToArray();
        var totals = DiscoveryTotal().Matches(output.Replace("\r", "", StringComparison.Ordinal));

        return
            totals.Count == 1
            && int.Parse(totals[0].Groups[1].Value, CultureInfo.InvariantCulture) == names.Length
            && names.Length >= minimum
            && names.Distinct(StringComparer.Ordinal).Count() == names.Length
            ? Result.Ok(names)
            : Result.Fail<string[]>("Incomplete or duplicate MTP discovery.");
    }

    internal static Result<int> Tests(XDocument document, string[] discovered)
    {
        var assemblies = document.Root?.Elements("assembly").ToArray() ?? [];
        var tests = document.Descendants("test").ToArray();
        var names = tests
            .Select(t => (string?)t.Attribute("name") ?? "")
            .Order(StringComparer.Ordinal);

        return
            assemblies.Length == 1
            && (string?)assemblies[0].Attribute("errors") == "0"
            && (string?)assemblies[0].Attribute("total")
                == discovered.Length.ToString(CultureInfo.InvariantCulture)
            && names.SequenceEqual(discovered.Select(EscapeTestName).Order(StringComparer.Ordinal))
            && tests.All(t => (string?)t.Attribute("result") == "Pass")
            ? Result.Ok(tests.Length)
            : Result.Fail<int>(
                "Executed tests differ from discovery, or contain errors, failures or skips."
            );
    }

    internal static Result Audit(JsonElement document, int projectCount)
    {
        if (
            !document.TryGetProperty("projects", out var projects)
            || projects.GetArrayLength() != projectCount
            || (
                document.TryGetProperty("problems", out var problems)
                && problems.GetArrayLength() > 0
            )
        )
            return Result.Fail("Incomplete dependency audit.");

        foreach (var project in projects.EnumerateArray())
        {
            if (!project.TryGetProperty("frameworks", out var frameworks))
                continue;

            foreach (var framework in frameworks.EnumerateArray())
            foreach (var group in new[] { "topLevelPackages", "transitivePackages" })
            {
                if (
                    framework.TryGetProperty(group, out var packages)
                    && packages
                        .EnumerateArray()
                        .Any(p =>
                            p.TryGetProperty("vulnerabilities", out var v) && v.GetArrayLength() > 0
                        )
                )
                    return Result.Fail("Known vulnerable dependency blocks analysis.");
            }
        }

        return Result.Ok();
    }

    internal static string EscapeTestName(string name)
    {
        var escaped = new StringBuilder();

        for (var index = 0; index < name.Length; index++)
        {
            var character = name[index];
            if (char.IsSurrogatePair(name, index))
            {
                escaped.Append(character).Append(name[++index]);
                continue;
            }

            escaped.Append(
                character switch
                {
                    '\\' => "\\\\",
                    '"' => "\\\"",
                    '\0' => "\\0",
                    '\a' => "\\a",
                    '\b' => "\\b",
                    '\f' => "\\f",
                    '\n' => "\\n",
                    '\r' => "\\r",
                    '\t' => "\\t",
                    '\v' => "\\v",
                    < ' ' => "\\x" + ((int)character).ToString("x2", CultureInfo.InvariantCulture),
                    _ when char.IsSurrogate(character) || character is '\ufffe' or '\uffff' => "\\x"
                        + ((int)character).ToString("x4", CultureInfo.InvariantCulture),
                    _ => character.ToString(),
                }
            );
        }

        return escaped.ToString();
    }

    internal static async Task<Result<CoverageEvidence>> CoverageAsync(
        XDocument document,
        string root,
        string[] anchors,
        IReadOnlyDictionary<string, string>? verified,
        CancellationToken cancellationToken
    )
    {
        var mapped = new SortedSet<string>(StringComparer.Ordinal);
        var executed = new SortedSet<string>(StringComparer.Ordinal);
        var checksums = new Dictionary<string, string>(StringComparer.Ordinal);

        foreach (var module in document.Descendants("module"))
        {
            var name = (string?)module.Attribute("name") ?? "";
            if (name.EndsWith(".dll", StringComparison.OrdinalIgnoreCase))
                name = name[..^4];

            if (
                !ProjectFolders.Any(folder =>
                    File.Exists(Path.Combine(root, folder, name, name + ".csproj"))
                )
            )
                continue;

            var ids = new Dictionary<string, string>(StringComparer.Ordinal);
            foreach (var source in module.Descendants("source_file"))
            {
                var path = (string?)source.Attribute("path") ?? "";
                var relative = SourceFiles.Relative(root, path);
                if (relative.Split('/').Contains("obj"))
                    continue;

                var data = await File.ReadAllBytesAsync(path, cancellationToken);
                var sha256 = SourceFiles.Hash(data);
                var algorithm = (
                    (string?)source.Attribute("checksum_type") ?? ""
                ).ToUpperInvariant();
                var expected = (string?)source.Attribute("checksum");
                var valid = algorithm switch
                {
                    "SHA256" => string.Equals(sha256, expected, StringComparison.OrdinalIgnoreCase),
                    "" => verified is not null
                        && verified.TryGetValue(relative, out var previous)
                        && previous == sha256,
                    _ => false,
                };
                if (!valid)
                    return Result.Fail<CoverageEvidence>("Unverified coverage source: " + relative);

                checksums[relative] = sha256;
                ids[(string?)source.Attribute("id") ?? ""] = relative;
            }

            foreach (var range in module.Descendants("range"))
            {
                if (
                    !ids.TryGetValue((string?)range.Attribute("source_id") ?? "", out var relative)
                    || !ProductionPrefixes.Any(prefix =>
                        relative.StartsWith(prefix, StringComparison.Ordinal)
                    )
                )
                    continue;

                mapped.Add(relative);
                if ((string?)range.Attribute("covered") == "yes")
                    executed.Add(relative);
            }
        }

        return mapped.Count > 0 && executed.Count > 0 && anchors.All(executed.Contains)
            ? Result.Ok(new CoverageEvidence([.. mapped], [.. executed], checksums))
            : Result.Fail<CoverageEvidence>(
                "Coverage lacks executed production sources or required child-process anchors."
            );
    }

    internal static async Task<Result<string[]>> ProjectScopeAsync(
        string scannerDirectory,
        string root,
        string[] projects,
        CancellationToken cancellationToken
    )
    {
        var found = new SortedSet<string>(StringComparer.Ordinal);

        foreach (
            var file in Directory.EnumerateFiles(
                Path.Combine(scannerDirectory, "out"),
                "ProjectInfo.xml",
                SearchOption.AllDirectories
            )
        )
        {
            var document = XDocument.Parse(await File.ReadAllTextAsync(file, cancellationToken));
            var values = document
                .Root!.Elements()
                .ToDictionary(e => e.Name.LocalName, e => e.Value, StringComparer.Ordinal);
            if (values.GetValueOrDefault("IsExcluded") != "false")
                return Result.Fail<string[]>("Scanner excluded a solution project.");

            found.Add(SourceFiles.Relative(root, values["FullPath"]));
        }

        return found.SetEquals(projects)
            ? Result.Ok(found.ToArray())
            : Result.Fail<string[]>("Scanner scope differs from the full solution.");
    }

    [GeneratedRegex(
        "^Discovered (\\d+) tests\\.$",
        RegexOptions.Multiline | RegexOptions.CultureInvariant
    )]
    private static partial Regex DiscoveryTotal();
}
