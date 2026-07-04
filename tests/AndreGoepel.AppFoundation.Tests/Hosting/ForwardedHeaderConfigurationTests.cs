using AndreGoepel.AppFoundation.Hosting;
using Microsoft.Extensions.Configuration;

namespace AndreGoepel.AppFoundation.Tests.Hosting;

public class ForwardedHeaderConfigurationTests
{
    [Fact]
    public void Merge_DelimitedScalar_SplitsIntoEntries()
    {
        // Arrange
        var configuration = BuildConfiguration(
            new()
            {
                ["AppFoundation:KnownProxyNetworks"] = "172.28.0.0/16, 10.0.0.0/8",
                ["AppFoundation:KnownProxies"] = "192.168.1.1;192.168.1.2",
            }
        );
        var options = new AppFoundationOptions();

        // Act
        Initialization.MergeForwardedHeaderConfiguration(configuration, options);

        // Assert
        Assert.Equal(new[] { "172.28.0.0/16", "10.0.0.0/8" }, options.KnownProxyNetworks);
        Assert.Equal(new[] { "192.168.1.1", "192.168.1.2" }, options.KnownProxies);
    }

    [Fact]
    public void Merge_IPv6Cidr_IsNotSplitOnColon()
    {
        // Arrange
        var configuration = BuildConfiguration(
            new() { ["AppFoundation:KnownProxyNetworks"] = "fd00::/8, 172.28.0.0/16" }
        );
        var options = new AppFoundationOptions();

        // Act
        Initialization.MergeForwardedHeaderConfiguration(configuration, options);

        // Assert
        Assert.Equal(new[] { "fd00::/8", "172.28.0.0/16" }, options.KnownProxyNetworks);
    }

    [Fact]
    public void Merge_ArrayForm_BindsEachEntry()
    {
        // Arrange
        var configuration = BuildConfiguration(
            new()
            {
                ["AppFoundation:KnownProxyNetworks:0"] = "10.0.0.0/8",
                ["AppFoundation:KnownProxyNetworks:1"] = "172.16.0.0/12",
            }
        );
        var options = new AppFoundationOptions();

        // Act
        Initialization.MergeForwardedHeaderConfiguration(configuration, options);

        // Assert
        Assert.Equal(new[] { "10.0.0.0/8", "172.16.0.0/12" }, options.KnownProxyNetworks);
    }

    [Fact]
    public void Merge_ConfigAugmentsCode_AndDeduplicates()
    {
        // Arrange — one network set in code, one added (plus a duplicate) via config.
        var configuration = BuildConfiguration(
            new() { ["AppFoundation:KnownProxyNetworks"] = "10.0.0.0/8, 172.28.0.0/16" }
        );
        var options = new AppFoundationOptions();
        options.KnownProxyNetworks.Add("10.0.0.0/8");

        // Act
        Initialization.MergeForwardedHeaderConfiguration(configuration, options);

        // Assert — code entry kept, new one appended, duplicate not added twice.
        Assert.Equal(new[] { "10.0.0.0/8", "172.28.0.0/16" }, options.KnownProxyNetworks);
    }

    [Fact]
    public void Merge_NoConfiguration_LeavesOptionsUnchanged()
    {
        // Arrange
        var configuration = BuildConfiguration([]);
        var options = new AppFoundationOptions();

        // Act
        Initialization.MergeForwardedHeaderConfiguration(configuration, options);

        // Assert
        Assert.Empty(options.KnownProxyNetworks);
        Assert.Empty(options.KnownProxies);
    }

    private static IConfiguration BuildConfiguration(Dictionary<string, string?> values) =>
        new ConfigurationBuilder().AddInMemoryCollection(values).Build();
}
