using System.Globalization;
using System.Text.Json;
using AndreGoepel.Core;

namespace AndreGoepel.AppFoundation.Sonar;

internal sealed class MainSonarPolicy(
    ISonarClient client,
    MainPolicy policy,
    string gateName,
    string scanToken,
    string verifyToken
)
{
    private static readonly string[] ProfileTimestampFormats =
    [
        "yyyy-MM-dd'T'HH:mm:ss.FFFFFFFzzz",
        "yyyy-MM-dd'T'HH:mm:ss.FFFFFFF'Z'",
    ];

    internal async Task<Result> CheckDefinitionAsync(CancellationToken ct)
    {
        var gate = await client.GetAsync("qualitygates/show", verifyToken, ct, ("name", gateName));
        var conditions = gate.GetProperty("conditions")
            .EnumerateArray()
            .Select(condition => new GateCondition(
                condition.GetProperty("metric").GetString()!,
                condition.GetProperty("op").GetString()!,
                condition.GetProperty("error").GetString()!
            ))
            .ToArray();
        if (
            !conditions
                .OrderBy(c => c.Metric, StringComparer.Ordinal)
                .SequenceEqual(policy.Conditions.OrderBy(c => c.Metric, StringComparer.Ordinal))
        )
            return Result.Fail("The central gate definition differs from the reviewed policy.");

        var profiles = await client.GetAsync(
            "qualityprofiles/search",
            verifyToken,
            ct,
            ("project", MainPolicy.Project)
        );
        var actual = ReadProfiles(profiles);
        if (actual.IsFailure)
            return Result.Fail(actual.Error!);

        if (
            !actual
                .Value!.OrderBy(p => p.Language, StringComparer.Ordinal)
                .SequenceEqual(policy.Profiles.OrderBy(p => p.Language, StringComparer.Ordinal))
        )
            return Result.Fail("Assigned profile names, rule counts or rule revisions changed.");

        var settings = await client.GetAsync(
            "settings/values",
            scanToken,
            ct,
            ("component", MainPolicy.Project),
            ("keys", "sonar.qualitygate.ignoreSmallChanges,sonar.coverage.exclusions")
        );
        var entries = settings.GetProperty("settings").EnumerateArray().ToArray();
        if (
            entries.Any(s =>
                s.GetProperty("key").GetString() == "sonar.coverage.exclusions"
                && (
                    (
                        s.TryGetProperty("value", out var text)
                        && !string.IsNullOrEmpty(text.GetString())
                    ) || (s.TryGetProperty("values", out var values) && values.GetArrayLength() > 0)
                )
            )
        )
            return Result.Fail(
                "Authored coverage exclusions are not allowed by the central policy."
            );

        if (
            !entries.Any(s =>
                s.GetProperty("key").GetString() == "sonar.qualitygate.ignoreSmallChanges"
                && s.GetProperty("value").GetString() == "false"
            )
        )
            return Result.Fail("The small-change exemption must be disabled.");

        return Result.Ok();
    }

    private static Result<ProfilePolicy[]> ReadProfiles(JsonElement response)
    {
        var profiles = new List<ProfilePolicy>();
        foreach (var profile in response.GetProperty("profiles").EnumerateArray())
        {
            if (
                !DateTimeOffset.TryParseExact(
                    profile.GetProperty("rulesUpdatedAt").GetString(),
                    ProfileTimestampFormats,
                    CultureInfo.InvariantCulture,
                    DateTimeStyles.AssumeUniversal,
                    out var rulesUpdatedAt
                )
            )
                return Result.Fail<ProfilePolicy[]>(
                    "A central profile has an invalid rule revision timestamp."
                );

            profiles.Add(
                new ProfilePolicy(
                    profile.GetProperty("language").GetString()!,
                    profile.GetProperty("name").GetString()!,
                    profile.GetProperty("activeRuleCount").GetInt32(),
                    rulesUpdatedAt
                )
            );
        }

        return Result.Ok(profiles.ToArray());
    }

    internal async Task<Result> VerifyAnalysisAsync(
        AnalysisSummary summary,
        JsonElement latest,
        MainRunIdentity identity,
        CancellationToken ct
    )
    {
        if (
            latest.GetProperty("key").GetString() != summary.AnalysisId
            || latest.GetProperty("revision").GetString() != identity.Sha
            || latest.GetProperty("buildString").GetString() != identity.BuildString
            || !DateTimeOffset.TryParse(
                latest.GetProperty("date").GetString(),
                CultureInfo.InvariantCulture,
                out var analyzedAt
            )
            || analyzedAt < summary.StartedAt.AddSeconds(-1)
        )
            return Result.Fail(
                "Sonar analysis does not match this source revision and workflow attempt."
            );

        if (string.IsNullOrEmpty(policy.BaselineAnalysisId))
            return Result.Fail(
                "Full analysis retained; a reviewed SPECIFIC_ANALYSIS baseline is still required."
            );

        var baseline = await client.GetAsync(
            "new_code_periods/show",
            scanToken,
            ct,
            ("project", MainPolicy.Project),
            ("branch", "main")
        );
        if (
            baseline.GetProperty("type").GetString() != "SPECIFIC_ANALYSIS"
            || baseline.GetProperty("value").GetString() != policy.BaselineAnalysisId
            || policy.BaselineAnalysisId == summary.AnalysisId
        )
            return Result.Fail(
                "The new-code baseline is missing, changed or refers to this analysis."
            );

        var measures = await client.GetAsync(
            "measures/component",
            verifyToken,
            ct,
            ("component", MainPolicy.Project),
            (
                "metricKeys",
                "new_lines,new_lines_to_cover,new_conditions_to_cover,new_security_hotspots"
            )
        );
        foreach (
            var measure in measures
                .GetProperty("component")
                .GetProperty("measures")
                .EnumerateArray()
        )
        {
            var value = measure.TryGetProperty("value", out var overall)
                ? overall.GetString()
                : measure.GetProperty("period").GetProperty("value").GetString();
            summary.Measures[measure.GetProperty("metric").GetString()!] = value!;
        }

        return EvaluateGate(summary.Gate!.Value, policy.Conditions, summary.Measures);
    }

    internal static Result EvaluateGate(
        JsonElement gate,
        GateCondition[] policy,
        IReadOnlyDictionary<string, string> measures
    )
    {
        if (
            gate.GetProperty("status").GetString() != "OK"
            || gate.GetProperty("ignoredConditions").GetBoolean()
        )
            return Result.Fail("The gate failed or ignored conditions.");

        var conditions = gate.GetProperty("conditions").EnumerateArray().ToArray();
        if (
            conditions.Length == 0
            || conditions.Any(c =>
                !policy.Any(p => p.Metric == c.GetProperty("metricKey").GetString())
            )
        )
            return Result.Fail("Empty or unknown evaluated conditions.");

        foreach (var expected in policy)
        {
            var found = conditions
                .Where(c => c.GetProperty("metricKey").GetString() == expected.Metric)
                .ToArray();
            if (found.Length == 0 && HasZeroDenominator(expected.Metric, measures))
                continue;

            if (
                found.Length != 1
                || found[0].GetProperty("status").GetString() != "OK"
                || found[0].GetProperty("comparator").GetString() != expected.Op
                || found[0].GetProperty("errorThreshold").GetString() != expected.Error
                || !decimal.TryParse(
                    found[0].GetProperty("actualValue").GetString(),
                    CultureInfo.InvariantCulture,
                    out var actual
                )
                || !decimal.TryParse(
                    expected.Error,
                    CultureInfo.InvariantCulture,
                    out var threshold
                )
                || actual < 0
                || (expected.Op == "LT" ? actual < threshold : actual > threshold)
            )
                return Result.Fail(
                    "A required gate condition failed, is absent or differs from the policy."
                );
        }

        return Result.Ok();
    }

    private static bool HasZeroDenominator(
        string metric,
        IReadOnlyDictionary<string, string> measures
    ) =>
        metric switch
        {
            "new_coverage" => IsZero(measures, "new_lines_to_cover")
                && IsZero(measures, "new_conditions_to_cover"),
            "new_duplicated_lines_density" => IsZero(measures, "new_lines"),
            "new_security_hotspots_reviewed" => IsZero(measures, "new_security_hotspots"),
            _ => false,
        };

    private static bool IsZero(IReadOnlyDictionary<string, string> measures, string key) =>
        measures.TryGetValue(key, out var text)
        && decimal.TryParse(text, CultureInfo.InvariantCulture, out var value)
        && value == 0;
}
