using System.Text.Json.Nodes;
using AndreGoepel.AppFoundation.Sonar;
using Xunit;

namespace AndreGoepel.AppFoundation.Sonar.Tests;

public sealed class MainSonarPolicyTests
{
    private static readonly string[] AllSources = ["**/*"];

    [Fact]
    public async Task CheckDefinition_ReviewedPolicy_AcceptsWithoutAdministrativeAccess()
    {
        // Arrange
        var fixture = new MainFixture();

        // Act
        var result = await fixture
            .Sonar()
            .CheckDefinitionAsync(TestContext.Current.CancellationToken);

        // Assert
        Assert.True(result.IsSuccess, result.Error);
        Assert.Equal(
            ["qualitygates/show", "qualityprofiles/search", "settings/values"],
            fixture.Requests
        );
    }

    [Theory]
    [InlineData("2026-10-16T17:32:20+0800")]
    [InlineData("2026-10-16T04:02:20-0530")]
    [InlineData("2026-10-16T09:32:20+0000")]
    [InlineData("2026-10-16T17:32:20+08:00")]
    [InlineData("2026-10-16T09:32:20Z")]
    [InlineData("2026-10-16T09:32:20.0000000+00:00")]
    public async Task CheckDefinition_EquivalentRuleRevisionFormats_Accepts(string timestamp)
    {
        // Arrange
        var fixture = new MainFixture();
        fixture.Responses["qualityprofiles/search"]["profiles"]![0]!["rulesUpdatedAt"] = timestamp;

        // Act
        var result = await fixture
            .Sonar()
            .CheckDefinitionAsync(TestContext.Current.CancellationToken);

        // Assert
        Assert.True(result.IsSuccess, result.Error);
    }

    [Theory]
    [InlineData(null)]
    [InlineData("invalid")]
    [InlineData("2026-10-16")]
    [InlineData("2026-10-16T09:32:20")]
    [InlineData("2026-10-16T17:32:20+2500")]
    [InlineData("2026-13-16T17:32:20+0800")]
    public async Task CheckDefinition_InvalidRuleRevision_Rejects(string? timestamp)
    {
        // Arrange
        var fixture = new MainFixture();
        fixture.Responses["qualityprofiles/search"]["profiles"]![0]!["rulesUpdatedAt"] = timestamp;

        // Act
        var result = await fixture
            .Sonar()
            .CheckDefinitionAsync(TestContext.Current.CancellationToken);

        // Assert
        Assert.True(result.IsFailure);
        Assert.Equal("A central profile has an invalid rule revision timestamp.", result.Error);
    }

    [Theory]
    [InlineData("threshold")]
    [InlineData("metric")]
    [InlineData("profile-name")]
    [InlineData("profile-count")]
    [InlineData("profile-revision")]
    [InlineData("exemption")]
    [InlineData("exclusions")]
    public async Task CheckDefinition_PolicyDrift_Rejects(string change)
    {
        // Arrange
        var fixture = new MainFixture();
        switch (change)
        {
            case "threshold":
                fixture.Responses["qualitygates/show"]["conditions"]![0]!["error"] = "100";
                break;
            case "metric":
                fixture.Responses["qualitygates/show"]["conditions"]![0]!["metric"] = "other";
                break;
            case "profile-name":
                fixture.Responses["qualityprofiles/search"]["profiles"]![0]!["name"] = "other";
                break;
            case "profile-count":
                fixture.Responses["qualityprofiles/search"]["profiles"]![0]!["activeRuleCount"] = 0;
                break;
            case "profile-revision":
                fixture.Responses["qualityprofiles/search"]["profiles"]![0]!["rulesUpdatedAt"] =
                    "2026-10-16T17:32:21+0800";
                break;
            case "exemption":
                fixture.Responses["settings/values"]["settings"]![0]!["value"] = "true";
                break;
            case "exclusions":
                fixture.Responses["settings/values"]["settings"]!
                    .AsArray()
                    .Add(
                        MainFixture.Node(
                            new { key = "sonar.coverage.exclusions", values = AllSources }
                        )
                    );
                break;
        }

        // Act
        var result = await fixture
            .Sonar()
            .CheckDefinitionAsync(TestContext.Current.CancellationToken);

        // Assert
        Assert.True(result.IsFailure);
    }

    [Fact]
    public async Task Verify_ExactAnalysisAndBaseline_AcceptsAllConditions()
    {
        // Arrange
        var fixture = new MainFixture();
        var summary = fixture.Summary();

        // Act
        var result = await fixture
            .Sonar()
            .VerifyAnalysisAsync(
                summary,
                MainFixture.Element(fixture.Latest()),
                fixture.Identity,
                TestContext.Current.CancellationToken
            );

        // Assert
        Assert.True(result.IsSuccess, result.Error);
        Assert.Equal("0", summary.Measures["new_lines"]);
    }

    [Theory]
    [InlineData("revision")]
    [InlineData("key")]
    [InlineData("buildString")]
    [InlineData("date")]
    [InlineData("pending-baseline")]
    [InlineData("baseline-type")]
    [InlineData("baseline-id")]
    [InlineData("self-baseline")]
    public async Task Verify_WrongAnalysisOrBaseline_Rejects(string change)
    {
        // Arrange
        var fixture = new MainFixture();
        var latest = fixture.Latest();
        var summary = fixture.Summary();
        switch (change)
        {
            case "revision":
            case "key":
                latest[change] = "wrong";
                break;
            case "buildString":
                latest[change] = "another-attempt";
                break;
            case "date":
                latest[change] = "2000-01-01T00:00:00Z";
                break;
            case "pending-baseline":
                fixture.Policy = fixture.Policy with { BaselineAnalysisId = null };
                break;
            case "baseline-type":
                fixture.Responses["new_code_periods/show"]["type"] = "PREVIOUS_VERSION";
                break;
            case "baseline-id":
                fixture.Responses["new_code_periods/show"]["value"] = "other";
                break;
            case "self-baseline":
                summary.AnalysisId = MainFixture.Baseline;
                break;
        }

        // Act
        var result = await fixture
            .Sonar()
            .VerifyAnalysisAsync(
                summary,
                MainFixture.Element(latest),
                fixture.Identity,
                TestContext.Current.CancellationToken
            );

        // Assert
        Assert.True(result.IsFailure);
    }

    [Theory]
    [InlineData(0)]
    [InlineData(1)]
    [InlineData(2)]
    [InlineData(3)]
    [InlineData(4)]
    [InlineData(5)]
    public void Evaluate_FailingCondition_RejectsEvenWhenOverallStatusClaimsSuccess(int index)
    {
        // Arrange
        var fixture = new MainFixture();
        var gate = fixture.Gate();
        gate["conditions"]![index]!["status"] = "ERROR";

        // Act
        var result = MainSonarPolicy.EvaluateGate(
            MainFixture.Element(gate),
            fixture.Policy.Conditions,
            new Dictionary<string, string>()
        );

        // Assert
        Assert.True(result.IsFailure);
    }

    [Theory]
    [InlineData("status", "ERROR")]
    [InlineData("comparator", "GT")]
    [InlineData("errorThreshold", "0")]
    [InlineData("actualValue", "79")]
    [InlineData("actualValue", "NaN")]
    [InlineData("actualValue", "-1")]
    public void Evaluate_MisleadingCoverageCondition_Rejects(string property, string value)
    {
        // Arrange
        var fixture = new MainFixture();
        var gate = fixture.Gate();
        gate["conditions"]![1]![property] = value;

        // Act
        var result = MainSonarPolicy.EvaluateGate(
            MainFixture.Element(gate),
            fixture.Policy.Conditions,
            new Dictionary<string, string>()
        );

        // Assert
        Assert.True(result.IsFailure);
    }

    [Theory]
    [InlineData("missing")]
    [InlineData("empty")]
    [InlineData("ignored")]
    [InlineData("duplicate")]
    [InlineData("unknown")]
    public void Evaluate_IncompleteOrIgnoredConditions_Rejects(string change)
    {
        // Arrange
        var fixture = new MainFixture();
        var gate = fixture.Gate();
        var conditions = gate["conditions"]!.AsArray();
        if (change == "missing")
            conditions.RemoveAt(1);
        else if (change == "empty")
            conditions.Clear();
        else if (change == "ignored")
            gate["ignoredConditions"] = true;
        else if (change == "unknown")
            conditions[0]!["metricKey"] = "other";
        else
            conditions.Add(conditions[0]!.DeepClone());

        // Act
        var result = MainSonarPolicy.EvaluateGate(
            MainFixture.Element(gate),
            fixture.Policy.Conditions,
            new Dictionary<string, string>()
        );

        // Assert
        Assert.True(result.IsFailure);
    }

    [Theory]
    [InlineData("0", true)]
    [InlineData("1", false)]
    [InlineData("unknown", false)]
    public void Evaluate_AbsentNewCoverage_RequiresExplicitZeroDenominators(
        string lines,
        bool accepted
    )
    {
        // Arrange
        var fixture = new MainFixture();
        var gate = fixture.Gate();
        gate["conditions"]!.AsArray().RemoveAt(3);
        var measures = new Dictionary<string, string>
        {
            ["new_lines_to_cover"] = lines,
            ["new_conditions_to_cover"] = "0",
        };

        // Act
        var result = MainSonarPolicy.EvaluateGate(
            MainFixture.Element(gate),
            fixture.Policy.Conditions,
            measures
        );

        // Assert
        Assert.Equal(accepted, result.IsSuccess);
    }
}
