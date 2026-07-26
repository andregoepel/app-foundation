using AndreGoepel.AppFoundation.Aspire;
using Aspire.Hosting;
using Aspire.Hosting.ApplicationModel;

namespace AndreGoepel.AppFoundation.Aspire.Tests;

public sealed class AppFoundationAspireExtensionsTests
{
    [Fact]
    public void AddStandardMailHog_Defaults_ExposesSmtpAndHttpEndpoints()
    {
        // Arrange
        var builder = DistributedApplication.CreateBuilder([]);

        // Act
        var mailhog = builder.AddStandardMailHog();

        // Assert
        Assert.Equal("mailhog", mailhog.Resource.Name);
        var endpoints = mailhog.Resource.Annotations.OfType<EndpointAnnotation>().ToList();
        var smtp = Assert.Single(endpoints, e => e.Name == "smtp");
        Assert.Equal(1025, smtp.Port);
        Assert.Equal(1025, smtp.TargetPort);
        var http = Assert.Single(endpoints, e => e.Name == "http");
        Assert.Equal(8025, http.Port);
        Assert.Equal(8025, http.TargetPort);
        Assert.Equal("http", http.UriScheme);
    }

    [Fact]
    public void AddStandardMailHog_CustomPorts_AppliesThemToTheHostSide()
    {
        // Arrange
        var builder = DistributedApplication.CreateBuilder([]);

        // Act
        var mailhog = builder.AddStandardMailHog(smtpPort: 11025, httpPort: 18025);

        // Assert
        var endpoints = mailhog.Resource.Annotations.OfType<EndpointAnnotation>().ToList();
        var smtp = Assert.Single(endpoints, e => e.Name == "smtp");
        Assert.Equal(11025, smtp.Port);
        Assert.Equal(1025, smtp.TargetPort);
        var http = Assert.Single(endpoints, e => e.Name == "http");
        Assert.Equal(18025, http.Port);
        Assert.Equal(8025, http.TargetPort);
    }

    [Fact]
    public void AddStandardPostgres_NotE2E_KeepsDataVolumeAndPersistentLifetime()
    {
        // Arrange
        var builder = DistributedApplication.CreateBuilder([]);

        // Act
        var (server, database) = builder.AddStandardPostgres(isE2E: false);

        // Assert
        Assert.Contains(server.Resource.Annotations, a => a is ContainerMountAnnotation);
        var lifetime = server.Resource.Annotations.OfType<ContainerLifetimeAnnotation>().Single();
        Assert.Equal(ContainerLifetime.Persistent, lifetime.Lifetime);
        Assert.Equal("appfoundation-database", database.Resource.Name);
        Assert.Equal("appfoundation-database", database.Resource.DatabaseName);
    }

    [Fact]
    public void AddStandardPostgres_E2E_SkipsDataVolumeAndPersistentLifetime()
    {
        // Arrange — E2E runs must get a fresh, throwaway database, never a developer's
        // persistent local data (mirrors the finance-app/marten-identity AppHost pattern).
        var builder = DistributedApplication.CreateBuilder([]);

        // Act
        var (server, _) = builder.AddStandardPostgres(isE2E: true);

        // Assert
        Assert.DoesNotContain(server.Resource.Annotations, a => a is ContainerMountAnnotation);
        Assert.DoesNotContain(server.Resource.Annotations, a => a is ContainerLifetimeAnnotation);
    }

    [Fact]
    public void AddStandardPostgres_ExplicitDatabaseName_DiffersFromResourceName()
    {
        // Arrange
        var builder = DistributedApplication.CreateBuilder([]);

        // Act
        var (_, database) = builder.AddStandardPostgres(
            isE2E: false,
            databaseResourceName: "appfoundation-database",
            databaseName: "appfoundation"
        );

        // Assert
        Assert.Equal("appfoundation-database", database.Resource.Name);
        Assert.Equal("appfoundation", database.Resource.DatabaseName);
    }
}
