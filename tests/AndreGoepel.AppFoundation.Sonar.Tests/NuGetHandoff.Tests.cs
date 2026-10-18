using System.Text.Json;
using System.Text.Json.Nodes;
using AndreGoepel.AppFoundation.Sonar;
using AndreGoepel.Core;
using Xunit;

namespace AndreGoepel.AppFoundation.Sonar.Tests;

public sealed class NuGetHandoffTests
{
    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task Verify_TransferredPreparedBatch_RechecksReadinessAndReturnsNonAuthorizingReceipt(
        bool annotated
    )
    {
        // Arrange
        using var fixture = new HandoffFixture();
        await fixture.InitializeAsync(annotated, TestContext.Current.CancellationToken);

        // Act
        var result = await fixture.VerifyAsync(TestContext.Current.CancellationToken);

        // Assert
        Assert.True(result.IsSuccess, result.Error);
        Assert.Equal(2, fixture.Checks);
        Assert.Equal(12, result.Value!.Packages.Length);
        Assert.True(result.Value.DryRun);
        Assert.False(result.Value.ReleaseEligible);
        Assert.True(
            fixture.Evidence.Main.Requests.Count(path => path == "git/ref/tags/v2.2.0") >= 2
        );
        Assert.Equal(
            6,
            result.Value.Packages.Select(package => package.PackageId).Distinct().Count()
        );
    }

    [Theory]
    [InlineData("missing-tag")]
    [InlineData("wrong-version-tag")]
    [InlineData("wrong-object")]
    [InlineData("invalid-object")]
    [InlineData("invalid-manifest-pin")]
    [InlineData("wrong-commit")]
    [InlineData("tag-cycle")]
    [InlineData("same-commit-retarget")]
    public async Task Verify_UntrustedTagBinding_BlocksBeforeReadiness(string fault)
    {
        // Arrange
        using var fixture = new HandoffFixture();
        await fixture.InitializeAsync(annotated: true, TestContext.Current.CancellationToken);
        switch (fault)
        {
            case "missing-tag":
                fixture.Input = fixture.Input with
                {
                    Release = fixture.Input.Release with { Tag = null },
                };
                break;
            case "wrong-version-tag":
                fixture.Input = fixture.Input with
                {
                    Release = fixture.Input.Release with { Tag = "v1.0.0" },
                };
                break;
            case "wrong-object":
                fixture.Input = fixture.Input with { TagObjectSha = new string('c', 40) };
                break;
            case "invalid-object":
                fixture.Input = fixture.Input with { TagObjectSha = "not-a-sha" };
                break;
            case "invalid-manifest-pin":
                fixture.Input = fixture.Input with { ManifestSha256 = "not-a-digest" };
                break;
            case "wrong-commit":
                fixture.Evidence.Main.Responses["git/tags/" + HandoffFixture.TagObject]["object"]![
                    "sha"
                ] = new string('f', 40);
                break;
            case "tag-cycle":
                fixture.Evidence.Main.Responses["git/tags/" + HandoffFixture.TagObject]["object"] =
                    MainFixture.Node(new { type = "tag", sha = HandoffFixture.TagObject });
                break;
            case "same-commit-retarget":
                fixture.Retarget();
                break;
        }

        // Act
        var result = await fixture.VerifyAsync(TestContext.Current.CancellationToken);

        // Assert
        Assert.True(result.IsFailure, fault);
        Assert.Equal(0, fixture.Checks);
    }

    [Theory]
    [InlineData("manifest-tampering")]
    [InlineData("missing-manifest")]
    [InlineData("oversized-manifest")]
    [InlineData("wrong-sha")]
    [InlineData("wrong-version")]
    [InlineData("wrong-run")]
    [InlineData("wrong-attempt")]
    [InlineData("expired")]
    [InlineData("extended-lifetime")]
    [InlineData("wrong-schema")]
    [InlineData("publication-flag")]
    [InlineData("not-dry-run")]
    [InlineData("missing-symbol")]
    [InlineData("duplicate-package")]
    [InlineData("wrong-id")]
    [InlineData("wrong-feed")]
    [InlineData("wrong-hash")]
    [InlineData("traversal")]
    [InlineData("null-package")]
    public async Task Verify_InvalidTransferManifest_BlocksBeforeFinalReadiness(string fault)
    {
        // Arrange
        using var fixture = new HandoffFixture();
        await fixture.InitializeAsync(annotated: false, TestContext.Current.CancellationToken);
        var manifest = JsonNode.Parse(
            await File.ReadAllTextAsync(fixture.ManifestPath, TestContext.Current.CancellationToken)
        )!;
        var packages = (JsonArray)manifest["packages"]!;
        switch (fault)
        {
            case "wrong-sha":
                manifest["sha"] = new string('f', 40);
                break;
            case "wrong-version":
                manifest["version"] = "9.9.9";
                break;
            case "wrong-run":
                manifest["readinessRunId"] = 999;
                break;
            case "wrong-attempt":
                manifest["readinessAttempt"] = 2;
                break;
            case "expired":
                manifest["expiresAt"] = fixture.Evidence.Main.Now.AddMinutes(-1);
                break;
            case "extended-lifetime":
                manifest["expiresAt"] = fixture.Evidence.Main.Now.AddHours(1);
                break;
            case "wrong-schema":
                manifest["schemaVersion"] = 2;
                break;
            case "publication-flag":
                manifest["releaseEligible"] = true;
                break;
            case "not-dry-run":
                manifest["dryRun"] = false;
                break;
            case "missing-symbol":
                packages.RemoveAt(1);
                break;
            case "duplicate-package":
                packages[1] = packages[0]!.DeepClone();
                break;
            case "wrong-id":
                packages[0]!["packageId"] = "Unknown.Package";
                break;
            case "wrong-feed":
                packages[0]!["feed"] = "https://other-feed.example";
                break;
            case "wrong-hash":
                packages[0]!["sha256"] = "not-a-digest";
                break;
            case "traversal":
                packages[0]!["file"] = "../package.nupkg";
                break;
            case "null-package":
                packages[0] = null;
                break;
        }
        await File.WriteAllBytesAsync(
            fixture.ManifestPath,
            ReleaseFixture.Bytes(manifest),
            TestContext.Current.CancellationToken
        );
        if (fault == "missing-manifest")
            File.Delete(fixture.ManifestPath);
        else if (fault == "oversized-manifest")
            await File.WriteAllTextAsync(
                fixture.ManifestPath,
                new string('x', 65537),
                TestContext.Current.CancellationToken
            );
        else if (fault != "manifest-tampering")
            await fixture.PinManifestAsync(TestContext.Current.CancellationToken);
        else
            await File.AppendAllTextAsync(
                fixture.ManifestPath,
                " ",
                TestContext.Current.CancellationToken
            );

        // Act
        var result = await fixture.VerifyAsync(TestContext.Current.CancellationToken);

        // Assert
        Assert.True(result.IsFailure, fault);
        Assert.Equal(1, fixture.Checks);
    }

    [Theory]
    [InlineData("package")]
    [InlineData("symbol")]
    [InlineData("missing-file")]
    [InlineData("extra-file")]
    [InlineData("nested-directory")]
    public async Task Verify_TransferContentsChanged_RejectsTheWholeBatch(string fault)
    {
        // Arrange
        using var fixture = new HandoffFixture();
        await fixture.InitializeAsync(annotated: false, TestContext.Current.CancellationToken);
        fixture.ChangeFiles(fault);

        // Act
        var result = await fixture.VerifyAsync(TestContext.Current.CancellationToken);

        // Assert
        Assert.True(result.IsFailure, fault);
        Assert.Equal(1, fixture.Checks);
    }

    [Theory]
    [InlineData("package")]
    [InlineData("symbol")]
    [InlineData("missing-file")]
    [InlineData("extra-file")]
    [InlineData("nested-directory")]
    [InlineData("retarget")]
    [InlineData("failed-gate")]
    [InlineData("main-moved")]
    [InlineData("manifest-tampering")]
    [InlineData("expired")]
    public async Task Verify_ChangesDuringFinalCheck_RejectsTheWholeBatch(string fault)
    {
        // Arrange
        using var fixture = new HandoffFixture();
        await fixture.InitializeAsync(annotated: true, TestContext.Current.CancellationToken);
        fixture.OnVerify = () =>
        {
            if (fixture.Checks != 2)
                return;
            switch (fault)
            {
                case "retarget":
                    fixture.Retarget();
                    break;
                case "failed-gate":
                    fixture.Evidence.Main.Responses["qualitygates/project_status"][
                        "projectStatus"
                    ]!["status"] = "ERROR";
                    break;
                case "main-moved":
                    fixture.Evidence.Main.Responses["git/ref/heads/main"]["object"]!["sha"] =
                        new string('f', 40);
                    break;
                case "manifest-tampering":
                    File.AppendAllText(fixture.ManifestPath, " ");
                    break;
                case "expired":
                    fixture.Clock.Now = fixture.Evidence.Main.Now.AddHours(1);
                    break;
                default:
                    fixture.ChangeFiles(fault);
                    break;
            }
        };

        // Act
        var result = await fixture.VerifyAsync(TestContext.Current.CancellationToken);

        // Assert
        Assert.True(result.IsFailure, fault);
        Assert.Equal(2, fixture.Checks);
    }

    [Fact]
    public async Task Verify_ExpiredRetry_DoesNotReuseEarlierSuccess()
    {
        // Arrange
        using var fixture = new HandoffFixture();
        await fixture.InitializeAsync(annotated: false, TestContext.Current.CancellationToken);
        var first = await fixture.VerifyAsync(TestContext.Current.CancellationToken);
        Assert.True(first.IsSuccess, first.Error);
        fixture.Clock.Now = fixture.Evidence.Main.Now.AddHours(1);

        // Act
        var retry = await fixture.VerifyAsync(TestContext.Current.CancellationToken);

        // Assert
        Assert.True(retry.IsFailure);
        Assert.Equal(3, fixture.Checks);
    }

    private sealed class HandoffFixture : IReleaseReadiness, IDisposable
    {
        internal const string TagObject = "aaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaa";
        internal ReleaseFixture Evidence { get; } = new();
        internal NuGetHandoffInput Input { get; set; } = null!;
        internal NuGetAcceptanceFixture.AcceptanceClock Clock { get; } = new(DateTimeOffset.UtcNow);
        internal int Checks { get; private set; }
        internal Action? OnVerify { get; set; }
        internal string ManifestPath => Path.Combine(Input.Directory, "dry-run.json");

        internal async Task InitializeAsync(bool annotated, CancellationToken ct)
        {
            await Evidence.InitializeAsync(ct);
            var prepared = await new NuGetDryRun(
                Evidence.Verifier(),
                new NuGetPackageBuilder(
                    new NuGetPackageBuilderTests.BuildRunner(Evidence.Release, ""),
                    Evidence.Contract
                )
            ).RunAsync(Evidence.Release, ct);
            Assert.True(prepared.IsSuccess, prepared.Error);
            var transfer = Path.Combine(Evidence.Root, "artifacts/transfer");
            Directory.CreateDirectory(Path.Combine(transfer, "packages"));
            File.Copy(prepared.Value!, Path.Combine(transfer, "dry-run.json"));
            foreach (
                var file in Directory.GetFiles(
                    Path.Combine(Path.GetDirectoryName(prepared.Value!)!, "packages")
                )
            )
                File.Copy(file, Path.Combine(transfer, "packages", Path.GetFileName(file)));
            Evidence.Main.Responses["git/ref/tags/v2.2.0"] = MainFixture.Node(
                new
                {
                    @object = new
                    {
                        type = annotated ? "tag" : "commit",
                        sha = annotated ? TagObject : MainFixture.Sha,
                    },
                }
            );
            Evidence.Main.Responses["git/tags/" + TagObject] = MainFixture.Node(
                new { @object = new { type = "commit", sha = MainFixture.Sha } }
            );
            Input = new(
                Evidence.Release with
                {
                    Tag = "v2.2.0",
                },
                transfer,
                "",
                annotated ? TagObject : MainFixture.Sha
            );
            await PinManifestAsync(ct);
            Evidence.Main.Requests.Clear();
        }

        internal async Task PinManifestAsync(CancellationToken ct) =>
            Input = Input with
            {
                ManifestSha256 = SourceFiles.Hash(await File.ReadAllBytesAsync(ManifestPath, ct)),
            };

        internal void Retarget()
        {
            var changed = new string('b', 40);
            Evidence.Main.Responses["git/ref/tags/v2.2.0"]["object"]!["sha"] = changed;
            Evidence.Main.Responses["git/tags/" + changed] = MainFixture.Node(
                new { @object = new { type = "commit", sha = MainFixture.Sha } }
            );
        }

        internal void ChangeFiles(string fault)
        {
            var directory = Path.Combine(Input.Directory, "packages");
            switch (fault)
            {
                case "package":
                    File.WriteAllText(Directory.GetFiles(directory, "*.nupkg")[0], "tampered");
                    break;
                case "symbol":
                    File.WriteAllText(Directory.GetFiles(directory, "*.snupkg")[0], "tampered");
                    break;
                case "missing-file":
                    File.Delete(Directory.GetFiles(directory)[0]);
                    break;
                case "extra-file":
                    File.WriteAllText(Path.Combine(directory, "unexpected.nupkg"), "extra");
                    break;
                case "nested-directory":
                    Directory.CreateDirectory(Path.Combine(directory, "nested"));
                    break;
                default:
                    throw new ArgumentOutOfRangeException(nameof(fault));
            }
        }

        internal Task<Result<NuGetHandoffReceipt>> VerifyAsync(CancellationToken ct) =>
            new NuGetHandoff(this, Evidence.Main, Clock).VerifyAsync(Input, ct);

        public Task<Result<ReadinessReceipt>> VerifyAsync(
            NuGetRelease release,
            CancellationToken ct
        )
        {
            Checks++;
            OnVerify?.Invoke();
            return Evidence.Verifier().VerifyAsync(release, ct);
        }

        public void Dispose() => Evidence.Dispose();
    }
}
