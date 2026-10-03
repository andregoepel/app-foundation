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
        Assert.True(mailhog.Resource.TryGetContainerImageName(out var image));
        Assert.Equal(
            "docker.io/mailhog/mailhog:v1.0.1@sha256:8d76a3d4ffa32a3661311944007a415332c4bb855657f4f6c57996405c009bea",
            image
        );
        var endpoints = mailhog.Resource.Annotations.OfType<EndpointAnnotation>().ToList();
        var smtp = Assert.Single(endpoints, e => e.Name == "smtp");
        Assert.Equal(1025, smtp.Port);
        Assert.Equal(1025, smtp.TargetPort);
        var http = Assert.Single(endpoints, e => e.Name == "http");
        Assert.Equal(8025, http.Port);
        Assert.Equal(8025, http.TargetPort);
        Assert.Equal("http", http.UriScheme);
    }

    [Theory]
    [InlineData(null)]
    [InlineData("latest")]
    public void AddStandardMailHog_DefaultAliases_UseThePinnedImage(string? tag)
    {
        // Arrange
        var builder = DistributedApplication.CreateBuilder([]);

        // Act
        var mailhog = builder.AddStandardMailHog(tag: tag);

        // Assert
        Assert.True(mailhog.Resource.TryGetContainerImageName(out var image));
        Assert.Equal(
            "docker.io/mailhog/mailhog:v1.0.1@sha256:8d76a3d4ffa32a3661311944007a415332c4bb855657f4f6c57996405c009bea",
            image
        );
    }

    [Fact]
    public void AddStandardMailHog_CustomImageReference_PreservesTheOverride()
    {
        // Arrange
        var builder = DistributedApplication.CreateBuilder([]);
        var tag = "custom@sha256:" + new string('a', 64);

        // Act
        var mailhog = builder.AddStandardMailHog(tag: tag);

        // Assert
        Assert.True(mailhog.Resource.TryGetContainerImageName(out var image));
        Assert.Equal($"docker.io/mailhog/mailhog:{tag}", image);
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
        var mount = Assert.Single(server.Resource.Annotations.OfType<ContainerMountAnnotation>());
        Assert.Equal("/var/lib/postgresql", mount.Target);
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
        Assert.True(server.Resource.TryGetContainerImageName(out var image));
        Assert.Equal(
            "docker.io/library/postgres:18.4@sha256:a02db8cac496f15b094798a38254f14d6e00741f709360e5e00bb6668ea31636",
            image
        );
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
