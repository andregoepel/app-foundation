using System.Net;
using AndreGoepel.AppFoundation.Hosting;
using Microsoft.AspNetCore.HttpOverrides;

namespace AndreGoepel.AppFoundation.Tests.Hosting;

public class ForwardedHeadersOptionsTests
{
    [Fact]
    public void Build_AlwaysHonorsForwardedForAndProto()
    {
        // Arrange / Act
        var result = Initialization.BuildForwardedHeadersOptions(
            new AppFoundationOptions(),
            isDevelopment: false
        );

        // Assert
        Assert.Equal(
            ForwardedHeaders.XForwardedFor | ForwardedHeaders.XForwardedProto,
            result.ForwardedHeaders
        );
    }

    [Fact]
    public void Build_NonDevelopmentWithoutProxies_KeepsLoopbackOnlyDefault()
    {
        // Arrange / Act — no proxies configured: must NOT be wide open.
        var result = Initialization.BuildForwardedHeadersOptions(
            new AppFoundationOptions(),
            isDevelopment: false
        );

        // Assert — the framework loopback default is retained, so arbitrary clients
        // cannot spoof X-Forwarded-* headers.
        Assert.NotEmpty(result.KnownProxies);
        Assert.Contains(IPAddress.IPv6Loopback, result.KnownProxies);
    }

    [Fact]
    public void Build_DevelopmentWithoutProxies_TrustsAllOrigins()
    {
        // Arrange / Act
        var result = Initialization.BuildForwardedHeadersOptions(
            new AppFoundationOptions(),
            isDevelopment: true
        );

        // Assert — both trust lists cleared: forwarded headers accepted from anywhere.
        Assert.Empty(result.KnownIPNetworks);
        Assert.Empty(result.KnownProxies);
    }

    [Fact]
    public void Build_WithConfiguredProxies_TrustsExactlyThoseAndClearsDefaults()
    {
        // Arrange
        var options = new AppFoundationOptions();
        options.KnownProxyNetworks.Add("10.0.0.0/8");
        options.KnownProxies.Add("192.168.1.1");

        // Act — Development would otherwise trust all; explicit config wins.
        var result = Initialization.BuildForwardedHeadersOptions(options, isDevelopment: true);

        // Assert — exactly the configured entries, loopback defaults cleared.
        Assert.Equal(new[] { System.Net.IPNetwork.Parse("10.0.0.0/8") }, result.KnownIPNetworks);
        Assert.Equal(new[] { IPAddress.Parse("192.168.1.1") }, result.KnownProxies);
    }
}
