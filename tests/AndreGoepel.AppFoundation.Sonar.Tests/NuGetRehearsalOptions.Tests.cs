using AndreGoepel.AppFoundation.Sonar;
using Xunit;

namespace AndreGoepel.AppFoundation.Sonar.Tests;

public sealed class NuGetRehearsalOptionsTests
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
        var parsed = NuGetRehearsalOptions.Parse(args);

        // Assert
        Assert.True(parsed.IsSuccess, parsed.Error);
        Assert.Equal(Path.GetFullPath(fixture.Root), parsed.Value!.Input.Directory);
        Assert.Equal(500, parsed.Value.PreparationRunId);
        Assert.Equal(1, parsed.Value.PreparationAttempt);
        Assert.Equal("v2.2.0", parsed.Value.Input.Release.Tag);
        Assert.Equal(select ? [file] : null, parsed.Value.Files);
    }

    [Theory]
    [InlineData(4, "wrong-sha")]
    [InlineData(6, "2.2.0-preview")]
    [InlineData(8, "0")]
    [InlineData(10, "v9.9.9")]
    [InlineData(12, "relative")]
    [InlineData(14, "bad-digest")]
    [InlineData(16, "bad-object")]
    [InlineData(18, "0")]
    [InlineData(18, "-1")]
    [InlineData(18, "+500")]
    [InlineData(20, "0")]
    [InlineData(20, "2147483648")]
    public void Parse_InvalidIdentity_RejectsBeforeVerification(int index, string value)
    {
        // Arrange
        using var fixture = new RunnerFixture();
        var args = Arguments(fixture.Root);
        args[index] = value;

        // Act
        var parsed = NuGetRehearsalOptions.Parse(args);

        // Assert
        Assert.True(parsed.IsFailure);
        Assert.Equal(NuGetRehearsalOptions.Usage, parsed.Error);
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
        var parsed = NuGetRehearsalOptions.Parse([.. args, option, value]);

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
        var parsed = NuGetRehearsalOptions.Parse(args);

        // Assert
        Assert.True(parsed.IsFailure);
    }

    internal static string[] Arguments(string root) =>
        [
            "--root",
            root,
            "--nuget-rehearse",
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
