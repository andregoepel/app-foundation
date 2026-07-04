using AndreGoepel.AppFoundation.Hosting;
using AndreGoepel.Marten.Identity.Blazor;
using Microsoft.AspNetCore.Builder;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;

namespace AndreGoepel.AppFoundation.Tests.Hosting;

public class AddAppFoundationIdentityTests
{
    [Fact]
    public void AddAppFoundation_ConfigureIdentity_FlowsToBlazorOptions()
    {
        // Arrange
        var builder = CreateBuilder();

        // Act
        builder.AddAppFoundation(options =>
            options.ConfigureIdentity = identity =>
            {
                identity.EnableUserRegistration = false;
                identity.EnablePasskey = false;
            }
        );

        // Assert
        using var provider = builder.Services.BuildServiceProvider();
        var identity = provider.GetRequiredService<IOptions<MartenIdentityBlazorOptions>>().Value;
        Assert.False(identity.EnableUserRegistration);
        Assert.False(identity.EnablePasskey);
        Assert.True(identity.EnableTwoFactor); // untouched → keeps its default
    }

    [Fact]
    public void AddAppFoundation_WithoutConfigureIdentity_LeavesFullFeatureSetEnabled()
    {
        // Arrange
        var builder = CreateBuilder();

        // Act
        builder.AddAppFoundation();

        // Assert — the full login feature set is available unless a host opts out.
        using var provider = builder.Services.BuildServiceProvider();
        var identity = provider.GetRequiredService<IOptions<MartenIdentityBlazorOptions>>().Value;
        Assert.True(identity.EnableUserRegistration);
        Assert.True(identity.EnableTwoFactor);
        Assert.True(identity.EnablePasskey);
    }

    private static WebApplicationBuilder CreateBuilder()
    {
        var builder = WebApplication.CreateBuilder();
        builder.Configuration.AddInMemoryCollection(
            new Dictionary<string, string?>
            {
                ["ConnectionStrings:appfoundation-database"] =
                    "Host=localhost;Port=5432;Database=test;Username=u;Password=p",
            }
        );
        return builder;
    }
}
