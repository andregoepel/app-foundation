using AndreGoepel.AppFoundation.Hosting;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.HttpsPolicy;

namespace AndreGoepel.AppFoundation.Tests.Hosting;

public class SecurityHeadersTests
{
    [Fact]
    public void ConfigureHsts_Defaults_HardensBeyondFrameworkDefault()
    {
        // Arrange
        var hsts = new HstsOptions();

        // Act
        Initialization.ConfigureHsts(hsts, new AppFoundationOptions());

        // Assert
        Assert.Equal(TimeSpan.FromDays(365), hsts.MaxAge);
        Assert.True(hsts.IncludeSubDomains);
        Assert.True(hsts.Preload);
    }

    [Fact]
    public void ConfigureHsts_WithConfigureHstsCallback_CallbackOverridesDefaults()
    {
        // Arrange
        var hsts = new HstsOptions();
        var options = new AppFoundationOptions
        {
            ConfigureHsts = h => h.MaxAge = TimeSpan.FromDays(30),
        };

        // Act
        Initialization.ConfigureHsts(hsts, options);

        // Assert — callback runs after the foundation's own defaults, so it wins.
        Assert.Equal(TimeSpan.FromDays(30), hsts.MaxAge);
    }

    [Fact]
    public void ApplySecurityHeaders_Defaults_SetsAllThreeHeaders()
    {
        // Arrange
        var headers = new HeaderDictionary();

        // Act
        Initialization.ApplySecurityHeaders(headers, new AppFoundationOptions());

        // Assert
        Assert.Equal("nosniff", headers["X-Content-Type-Options"]);
        Assert.Equal("strict-origin-when-cross-origin", headers["Referrer-Policy"]);
        Assert.Equal("camera=(), microphone=(), geolocation=()", headers["Permissions-Policy"]);
    }

    [Fact]
    public void ApplySecurityHeaders_ReferrerPolicyClearedToNull_OmitsHeader()
    {
        // Arrange
        var headers = new HeaderDictionary();
        var options = new AppFoundationOptions { ReferrerPolicy = null };

        // Act
        Initialization.ApplySecurityHeaders(headers, options);

        // Assert — X-Content-Type-Options is unconditional, Referrer-Policy is not.
        Assert.True(headers.ContainsKey("X-Content-Type-Options"));
        Assert.False(headers.ContainsKey("Referrer-Policy"));
    }

    [Fact]
    public void ApplySecurityHeaders_PermissionsPolicyClearedToEmpty_OmitsHeader()
    {
        // Arrange
        var headers = new HeaderDictionary();
        var options = new AppFoundationOptions { PermissionsPolicy = string.Empty };

        // Act
        Initialization.ApplySecurityHeaders(headers, options);

        // Assert
        Assert.False(headers.ContainsKey("Permissions-Policy"));
    }
}
