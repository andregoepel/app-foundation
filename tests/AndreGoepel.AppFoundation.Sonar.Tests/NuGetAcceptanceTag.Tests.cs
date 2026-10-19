using System.Text.Json;
using AndreGoepel.AppFoundation.Sonar;
using Xunit;

namespace AndreGoepel.AppFoundation.Sonar.Tests;

public sealed class NuGetAcceptanceTagTests
{
    private const string Tag = "sonar-poc/nuget/0123456789abcdef0123456789abcdef/v2.2.0";
    private const string OtherTag = "sonar-poc/nuget/abcdef0123456789abcdef0123456789/v2.2.0";

    [Theory]
    [InlineData("--nuget-dry-run")]
    [InlineData("--nuget-prepare")]
    public void Parse_AcceptanceTag_PreservesFullIdentity(string mode)
    {
        // Arrange
        var args = Arguments(mode, Tag);

        // Act
        var result = NuGetRelease.Parse(args);

        // Assert
        Assert.True(result.IsSuccess, result.Error);
        Assert.Equal(Tag, result.Value!.Tag);
        Assert.Equal("2.2.0", result.Value.Version);
    }

    [Theory]
    [InlineData("version", "sonar-poc/nuget/0123456789abcdef0123456789abcdef/v2.2.1")]
    [InlineData("leading-zero", "sonar-poc/nuget/0123456789abcdef0123456789abcdef/v02.2.0")]
    [InlineData("prerelease", "sonar-poc/nuget/0123456789abcdef0123456789abcdef/v2.2.0-beta.1")]
    [InlineData("uppercase-id", "sonar-poc/nuget/0123456789ABCDEF0123456789ABCDEF/v2.2.0")]
    [InlineData("nonhex-id", "sonar-poc/nuget/0123456789abcdef0123456789abcdeg/v2.2.0")]
    [InlineData("short-id", "sonar-poc/nuget/short/v2.2.0")]
    [InlineData("long-id", "sonar-poc/nuget/0123456789abcdef0123456789abcdef0/v2.2.0")]
    [InlineData("missing-id", "sonar-poc/nuget/v2.2.0")]
    [InlineData("release-prefix", "v2.2.0/sonar-poc/nuget/0123456789abcdef0123456789abcdef")]
    [InlineData("uppercase-prefix", "Sonar-poc/nuget/0123456789abcdef0123456789abcdef/v2.2.0")]
    [InlineData("traversal", "sonar-poc/nuget/../v2.2.0")]
    [InlineData("extra-segment", Tag + "/extra")]
    [InlineData("newline", Tag + "\n")]
    [InlineData("full-ref", "refs/tags/" + Tag)]
    [InlineData("encoded", "sonar-poc%2Fnuget%2F0123456789abcdef0123456789abcdef%2Fv2.2.0")]
    public void Parse_UnknownOrAmbiguousAcceptanceTag_Rejects(string fault, string tag)
    {
        // Arrange
        var args = Arguments("--nuget-prepare", tag);

        // Act
        var result = NuGetRelease.Parse(args);

        // Assert
        Assert.True(result.IsFailure, fault);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task Prepare_AcceptanceTag_BindsOriginWithoutReleaseAuthority(bool annotated)
    {
        // Arrange
        using var fixture = new NuGetPreparationTests.PreparationFixture(Tag);
        await fixture.InitializeAsync(annotated, TestContext.Current.CancellationToken);

        // Act
        var result = await fixture.RunAsync(TestContext.Current.CancellationToken);

        // Assert
        Assert.True(result.IsSuccess, result.Error);
        var origin = JsonSerializer.Deserialize<NuGetPreparationOrigin>(
            await File.ReadAllBytesAsync(
                result.Value!.OriginPath,
                TestContext.Current.CancellationToken
            ),
            JsonFormat.Options
        )!;
        Assert.Equal(Tag, origin.Tag);
        Assert.Equal(fixture.Context.TagObjectSha, origin.TagObjectSha);
        Assert.True(origin.DryRun);
        Assert.False(origin.ReleaseEligible);
        Assert.Equal(12, Directory.GetFiles(Path.Combine(fixture.Batch!, "packages")).Length);
    }

    [Theory]
    [InlineData(Tag, "v2.2.0")]
    [InlineData("v2.2.0", Tag)]
    [InlineData(Tag, OtherTag)]
    public void Capture_SameVersionWithDifferentTag_Rejects(string expected, string actual)
    {
        // Arrange
        var release = NuGetPreparationContextTests.Release() with
        {
            Tag = expected,
        };
        var environment = NuGetPreparationContextTests.EnvironmentFor(release);
        var payload = NuGetPreparationContextTests.Payload(false, actual);

        // Act
        var result = NuGetPreparationContext.Capture(
            release,
            environment.GetValueOrDefault,
            NuGetPreparationContextTests.Element(payload)
        );

        // Assert
        Assert.True(result.IsFailure);
    }

    [Theory]
    [InlineData("retargeted")]
    [InlineData("peeled-event")]
    [InlineData("failed-gate")]
    public async Task Prepare_InvalidAcceptanceEvidence_StopsBeforePacking(string fault)
    {
        // Arrange
        using var fixture = new NuGetPreparationTests.PreparationFixture(Tag);
        await fixture.InitializeAsync(annotated: true, TestContext.Current.CancellationToken);
        if (fault == "retargeted")
            fixture.Retarget();
        else if (fault == "peeled-event")
            fixture.Context = fixture.Context with { TagObjectSha = fixture.Release.Sha };
        else
            fixture.Evidence.Main.Responses["qualitygates/project_status"]["projectStatus"]![
                "status"
            ] = "ERROR";

        // Act
        var result = await fixture.RunAsync(TestContext.Current.CancellationToken);

        // Assert
        Assert.True(result.IsFailure);
        Assert.Equal(0, fixture.Builds);
        Assert.Empty(fixture.Origins());
    }

    [Fact]
    public async Task Consume_AcceptanceTag_AuthenticatesAllFilesWithDiscardSink()
    {
        // Arrange
        using var fixture = new NuGetProvenanceTests.ProvenanceFixture(tag: Tag);
        await fixture.InitializeAsync(TestContext.Current.CancellationToken);
        var input = fixture.Handoff.Input;
        var args = Arguments("--nuget-rehearse", Tag);
        args[1] = input.Release.Root;
        var options = NuGetHandoffOptions.Parse([
            .. args,
            "--handoff",
            input.Directory,
            "--manifest-sha256",
            input.ManifestSha256,
            "--tag-object",
            input.TagObjectSha,
            "--preparation-run",
            "500",
            "--preparation-attempt",
            "1",
        ]);
        Assert.True(options.IsSuccess, options.Error);

        // Act
        var result = await fixture
            .Consumer()
            .RunAsync(options.Value!, TestContext.Current.CancellationToken);

        // Assert
        Assert.True(result.IsSuccess, result.Error);
        Assert.Equal(12, result.Value!.Accepted.Length);
        Assert.True(result.Value.DryRun);
        Assert.False(result.Value.ReleaseEligible);
        Assert.True(fixture.Downloads > 0);
    }

    [Theory]
    [InlineData("origin-release-tag")]
    [InlineData("origin-other-drill")]
    [InlineData("run")]
    [InlineData("artifact")]
    [InlineData("retargeted")]
    [InlineData("failed-gate")]
    [InlineData("tampered-package")]
    [InlineData("expired")]
    public async Task Consume_InvalidAcceptanceEvidence_StopsBeforeLogin(string fault)
    {
        // Arrange
        using var fixture = new NuGetProvenanceTests.ProvenanceFixture(tag: Tag);
        await fixture.InitializeAsync(TestContext.Current.CancellationToken);
        switch (fault)
        {
            case "origin-release-tag":
                fixture.Origin["tag"] = "v2.2.0";
                fixture.SealOrigin();
                break;
            case "origin-other-drill":
                fixture.Origin["tag"] = OtherTag;
                fixture.SealOrigin();
                break;
            case "run":
                fixture.Run["head_branch"] = "v2.2.0";
                break;
            case "artifact":
                fixture.Artifact["workflow_run"]!["head_branch"] = OtherTag;
                break;
            case "retargeted":
                fixture.Handoff.Retarget();
                break;
            case "failed-gate":
                fixture.Handoff.Evidence.Main.Responses["qualitygates/project_status"][
                    "projectStatus"
                ]!["status"] = "ERROR";
                break;
            case "tampered-package":
                fixture.Handoff.ChangeFiles("package");
                break;
            case "expired":
                fixture.Handoff.Clock.Now = fixture.Handoff.Clock.GetUtcNow().AddHours(2);
                break;
        }

        // Act
        var result = await fixture.RunAsync(TestContext.Current.CancellationToken);

        // Assert
        Assert.True(result.IsFailure);
        Assert.Empty(fixture.Sink.Events);
    }

    private static string[] Arguments(string mode, string tag) =>
        [
            "--root",
            Path.GetTempPath(),
            mode,
            "--sha",
            MainFixture.Sha,
            "--version",
            "2.2.0",
            "--readiness-run",
            "100",
            "--tag",
            tag,
        ];
}
