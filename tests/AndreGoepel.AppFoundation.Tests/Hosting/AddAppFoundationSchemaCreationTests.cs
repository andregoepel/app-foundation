using AndreGoepel.AppFoundation.Hosting;
using JasperFx;
using Marten;
using Microsoft.AspNetCore.Builder;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;

namespace AndreGoepel.AppFoundation.Tests.Hosting;

public class AddAppFoundationSchemaCreationTests
{
    [Fact]
    public void AddAppFoundation_NonDevelopmentEnvironment_UsesCreateOrUpdate()
    {
        // Arrange
        var builder = CreateBuilder("Production");

        // Act
        builder.AddAppFoundation();

        // Assert
        Assert.Equal(AutoCreate.CreateOrUpdate, ResolveAutoCreate(builder));
    }

    [Fact]
    public void AddAppFoundation_DevelopmentEnvironment_UsesAll()
    {
        // Arrange
        var builder = CreateBuilder("Development");

        // Act
        builder.AddAppFoundation();

        // Assert
        Assert.Equal(AutoCreate.All, ResolveAutoCreate(builder));
    }

    [Fact]
    public void AddAppFoundation_ExplicitSchemaCreation_OverridesEnvironmentDefault()
    {
        // Arrange — Development would otherwise select All.
        var builder = CreateBuilder("Development");

        // Act
        builder.AddAppFoundation(options => options.SchemaCreation = AutoCreate.None);

        // Assert
        Assert.Equal(AutoCreate.None, ResolveAutoCreate(builder));
    }

    private static AutoCreate ResolveAutoCreate(WebApplicationBuilder builder)
    {
        using var provider = builder.Services.BuildServiceProvider();
        var store = provider.GetRequiredService<IDocumentStore>();
        return ((StoreOptions)store.Options).AutoCreateSchemaObjects;
    }

    private static WebApplicationBuilder CreateBuilder(string environmentName)
    {
        var builder = WebApplication.CreateBuilder(
            new WebApplicationOptions { EnvironmentName = environmentName }
        );
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
