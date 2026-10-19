using AndreGoepel.AppFoundation.Sonar;
using Xunit;

namespace AndreGoepel.AppFoundation.Sonar.Tests;

public sealed class NuGetHandoffOptionsTests
{
    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void Parse_ExactArguments_PinsInputsAndOptionalSymbolSelection(bool select)
    {
        // Arrange
        using var fixture = new RunnerFixture();
        var file = "AndreGoepel.AppFoundation.2.2.0.snupkg";
        var args = Arguments(fixture.Root);
        if (select)
            args = [.. args, "--files", file];

        // Act
        var parsed = NuGetHandoffOptions.Parse(args);

        // Assert
        Assert.True(parsed.IsSuccess, parsed.Error);
        Assert.Equal(Path.GetFullPath(fixture.Root), parsed.Value!.Input.Directory);
        Assert.Equal(500, parsed.Value.PreparationRunId);
        Assert.Equal(1, parsed.Value.PreparationAttempt);
        Assert.Equal("v2.2.0", parsed.Value.Input.Release.Tag);
        Assert.Equal(select ? [file] : null, parsed.Value.Files);
        Assert.False(parsed.Value.VerifyOnly);
    }

    [Fact]
    public void Parse_VerificationMode_RequiresCompleteBatchWithoutSelection()
    {
        // Arrange
        using var fixture = new RunnerFixture();
        var args = Arguments(fixture.Root, "--nuget-verify-handoff");

        // Act
        var parsed = NuGetHandoffOptions.Parse(args);

        // Assert
        Assert.True(parsed.IsSuccess, parsed.Error);
        Assert.True(parsed.Value!.VerifyOnly);
        Assert.Null(parsed.Value.Files);
        Assert.Equal(500, parsed.Value.PreparationRunId);
        Assert.Equal(1, parsed.Value.PreparationAttempt);
    }

    [Theory]
    [InlineData("--files", "AndreGoepel.AppFoundation.2.2.0.snupkg")]
    [InlineData("--nuget-rehearse", "")]
    [InlineData("--publish", "true")]
    public void Parse_VerificationSelectionOrMixedMode_RejectsInput(string option, string value)
    {
        // Arrange
        using var fixture = new RunnerFixture();
        var args = Arguments(fixture.Root, "--nuget-verify-handoff");

        // Act
        var parsed = NuGetHandoffOptions.Parse([.. args, option, value]);

        // Assert
        Assert.True(parsed.IsFailure);
    }

    [Theory]
    [InlineData("--nuget-rehearse", 2, "--publish")]
    [InlineData("--nuget-rehearse", 4, "wrong-sha")]
    [InlineData("--nuget-rehearse", 6, "2.2.0-preview")]
    [InlineData("--nuget-rehearse", 8, "0")]
    [InlineData("--nuget-rehearse", 10, "v9.9.9")]
    [InlineData("--nuget-rehearse", 12, "relative")]
    [InlineData("--nuget-rehearse", 14, "bad-digest")]
    [InlineData("--nuget-rehearse", 16, "bad-object")]
    [InlineData("--nuget-rehearse", 18, "0")]
    [InlineData("--nuget-rehearse", 18, "-1")]
    [InlineData("--nuget-rehearse", 18, "+500")]
    [InlineData("--nuget-rehearse", 20, "0")]
    [InlineData("--nuget-rehearse", 20, "2147483648")]
    [InlineData("--nuget-verify-handoff", 4, "wrong-sha")]
    [InlineData("--nuget-verify-handoff", 6, "2.2.0-preview")]
    [InlineData("--nuget-verify-handoff", 8, "0")]
    [InlineData("--nuget-verify-handoff", 10, "v9.9.9")]
    [InlineData("--nuget-verify-handoff", 12, "relative")]
    [InlineData("--nuget-verify-handoff", 14, "bad-digest")]
    [InlineData("--nuget-verify-handoff", 16, "bad-object")]
    [InlineData("--nuget-verify-handoff", 18, "0")]
    [InlineData("--nuget-verify-handoff", 18, "-1")]
    [InlineData("--nuget-verify-handoff", 18, "+500")]
    [InlineData("--nuget-verify-handoff", 20, "0")]
    [InlineData("--nuget-verify-handoff", 20, "2147483648")]
    public void Parse_InvalidIdentity_RejectsBeforeVerification(
        string mode,
        int index,
        string value
    )
    {
        // Arrange
        using var fixture = new RunnerFixture();
        var args = Arguments(fixture.Root, mode);
        args[index] = value;

        // Act
        var parsed = NuGetHandoffOptions.Parse(args);

        // Assert
        Assert.True(parsed.IsFailure);
        Assert.Equal(NuGetHandoffOptions.Usage, parsed.Error);
    }

    [Theory]
    [InlineData("--publish", "true")]
    [InlineData("--policy", "untrusted.json")]
    [InlineData("--workflow-id", "900")]
    [InlineData("--source", "https://other.example")]
    [InlineData("--files", "../other.nupkg")]
    [InlineData("--files", "AndreGoepel.AppFoundation.9.9.9.nupkg")]
    [InlineData("--files", "--allow-stale")]
    public void Parse_UnsupportedOptionOrFile_RejectsInput(string option, string value)
    {
        // Arrange
        using var fixture = new RunnerFixture();
        var args = Arguments(fixture.Root);

        // Act
        var parsed = NuGetHandoffOptions.Parse([.. args, option, value]);

        // Assert
        Assert.True(parsed.IsFailure);
    }

    [Theory]
    [InlineData("missing")]
    [InlineData("empty-files")]
    [InlineData("duplicate-files")]
    public void Parse_IncompleteOrDuplicateArguments_RejectsInput(string fault)
    {
        // Arrange
        using var fixture = new RunnerFixture();
        var args = Arguments(fixture.Root);
        var file = "AndreGoepel.AppFoundation.2.2.0.nupkg";
        args = fault switch
        {
            "missing" => args[..^2],
            "empty-files" => [.. args, "--files"],
            _ => [.. args, "--files", file, file],
        };

        // Act
        var parsed = NuGetHandoffOptions.Parse(args);

        // Assert
        Assert.True(parsed.IsFailure);
    }

    internal static string[] Arguments(string root, string mode = "--nuget-rehearse") =>
        [
            "--root",
            root,
            mode,
            "--sha",
            MainFixture.Sha,
            "--version",
            "2.2.0",
            "--readiness-run",
            "100",
            "--tag",
            "v2.2.0",
            "--handoff",
            root,
            "--manifest-sha256",
            new string('a', 64),
            "--tag-object",
            MainFixture.Sha,
            "--preparation-run",
            "500",
            "--preparation-attempt",
            "1",
        ];
}
