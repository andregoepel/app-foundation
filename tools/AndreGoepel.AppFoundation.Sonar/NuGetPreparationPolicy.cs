using System.Text.Json;
using AndreGoepel.Core;

namespace AndreGoepel.AppFoundation.Sonar;

internal sealed record NuGetPreparationPolicy(
    long WorkflowId,
    string WorkflowSha256,
    int MaximumAgeMinutes,
    Dictionary<string, string[]> Jobs
)
{
    internal const string WorkflowPath = ".github/workflows/nuget-prepare.yml";
    internal const string ConfigurationPath = "scripts/sonar/nuget-policy.json";

    internal Result Validate() =>
        WorkflowId > 0
        && MaximumAgeMinutes is > 0 and <= 120
        && WorkflowSha256 is { Length: 64 }
        && WorkflowSha256.All(c => c is >= '0' and <= '9' or >= 'a' and <= 'f')
        && Jobs is { Count: > 0 }
        && Jobs.All(job =>
            !string.IsNullOrWhiteSpace(job.Key)
            && job.Value is { Length: > 0 }
            && job.Value.All(step => !string.IsNullOrWhiteSpace(step))
            && job.Value.Distinct(StringComparer.Ordinal).Count() == job.Value.Length
        )
            ? Result.Ok()
            : Result.Fail("A reviewed preparation policy is required.");

    internal static async Task<Result<NuGetPreparationPolicy>> ReadAsync(
        string root,
        CancellationToken ct
    )
    {
        var file = new FileInfo(Path.Combine(root, ConfigurationPath));
        if (
            !file.Exists
            || file.Length > 65536
            || (file.Attributes & FileAttributes.ReparsePoint) != 0
        )
            return Result.Fail<NuGetPreparationPolicy>(
                "A reviewed preparation policy is required."
            );

        var policy = JsonSerializer.Deserialize<NuGetPreparationPolicy>(
            await File.ReadAllTextAsync(file.FullName, ct),
            JsonFormat.Options
        );
        return policy is not null && policy.Validate().IsSuccess
            ? Result.Ok(policy)
            : Result.Fail<NuGetPreparationPolicy>("A reviewed preparation policy is required.");
    }
}
