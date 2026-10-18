using System.Text.Json;
using System.Text.Json.Nodes;
using AndreGoepel.AppFoundation.Sonar;
using Xunit;

namespace AndreGoepel.AppFoundation.Sonar.Tests;

public sealed class NuGetPreparationPolicyTests
{
    [Theory]
    [InlineData("workflowId", "0")]
    [InlineData("workflowSha256", "null")]
    [InlineData("workflowSha256", "\"invalid\"")]
    [InlineData("maximumAgeMinutes", "121")]
    [InlineData("jobs", "null")]
    [InlineData("jobs", "{}")]
    [InlineData("jobs", "{\"prepare\":null}")]
    [InlineData("jobs", "{\"prepare\":[]}")]
    [InlineData("jobs", "{\"prepare\":[\"\"]}")]
    [InlineData("jobs", "{\"prepare\":[null]}")]
    [InlineData("jobs", "{\"prepare\":[\"step\",\"step\"]}")]
    public async Task Read_UnregisteredOrMalformedPolicy_FailsClosed(string property, string json)
    {
        // Arrange
        using var fixture = new RunnerFixture();
        var policy = MainFixture.Node(Approved());
        policy[property] = JsonNode.Parse(json);
        await File.WriteAllTextAsync(
            Path.Combine(fixture.Root, NuGetPreparationPolicy.ConfigurationPath),
            policy.ToJsonString(),
            TestContext.Current.CancellationToken
        );

        // Act
        var result = await NuGetPreparationPolicy.ReadAsync(
            fixture.Root,
            TestContext.Current.CancellationToken
        );

        // Assert
        Assert.True(result.IsFailure);
    }

    [Theory]
    [InlineData("missing")]
    [InlineData("null")]
    [InlineData("oversized")]
    [InlineData("malformed")]
    [InlineData("valid")]
    public async Task Read_ConfigurationFile_RequiresReviewedBoundedDocument(string state)
    {
        // Arrange
        using var fixture = new RunnerFixture();
        var path = Path.Combine(fixture.Root, NuGetPreparationPolicy.ConfigurationPath);
        if (state != "missing")
            await File.WriteAllTextAsync(
                path,
                state switch
                {
                    "null" => "null",
                    "oversized" => new string(' ', 65537),
                    "malformed" => "{",
                    _ => JsonSerializer.Serialize(Approved(), JsonFormat.Options),
                },
                TestContext.Current.CancellationToken
            );

        // Act
        if (state == "malformed")
        {
            var error = await Record.ExceptionAsync(() =>
                NuGetPreparationPolicy.ReadAsync(
                    fixture.Root,
                    TestContext.Current.CancellationToken
                )
            );

            // Assert
            Assert.IsType<JsonException>(error);
            return;
        }
        var result = await NuGetPreparationPolicy.ReadAsync(
            fixture.Root,
            TestContext.Current.CancellationToken
        );

        // Assert
        Assert.Equal(state == "valid", result.IsSuccess);
    }

    internal static NuGetPreparationPolicy Approved() =>
        new(
            900,
            SourceFiles.Hash("reviewed-workflow"u8.ToArray()),
            120,
            new Dictionary<string, string[]>
            {
                ["prepare"] = ["Verify tag push", "Prepare NuGet batch", "Publish origin"],
            }
        );
}
