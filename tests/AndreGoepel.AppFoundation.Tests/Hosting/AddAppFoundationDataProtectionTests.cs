using System.Security.Cryptography;
using System.Security.Cryptography.X509Certificates;
using AndreGoepel.AppFoundation.Hosting;
using AndreGoepel.AppFoundation.Hosting.DataProtection;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.DataProtection;
using Microsoft.AspNetCore.DataProtection.KeyManagement;
using Microsoft.AspNetCore.DataProtection.XmlEncryption;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;

namespace AndreGoepel.AppFoundation.Tests.Hosting;

public class AddAppFoundationDataProtectionTests
{
    [Fact]
    public void AddAppFoundation_PersistsKeyRingViaMartenRepository()
    {
        // Arrange
        var builder = CreateBuilder();

        // Act
        builder.AddAppFoundation();

        // Assert
        using var provider = builder.Services.BuildServiceProvider();
        var options = provider.GetRequiredService<IOptions<KeyManagementOptions>>().Value;
        Assert.IsType<MartenXmlRepository>(options.XmlRepository);
    }

    [Fact]
    public void AddAppFoundation_WithoutCertificateConfig_StoresKeysWithoutEncryptor()
    {
        // Arrange
        var builder = CreateBuilder();

        // Act
        builder.AddAppFoundation();

        // Assert
        using var provider = builder.Services.BuildServiceProvider();
        var options = provider.GetRequiredService<IOptions<KeyManagementOptions>>().Value;
        Assert.Null(options.XmlEncryptor);
    }

    [Fact]
    public void AddAppFoundation_WithCertificateConfig_EncryptsKeysWithCertificate()
    {
        // Arrange
        var certificatePath = WriteSelfSignedPfx(password: "test-password");
        try
        {
            var builder = CreateBuilder(
                new Dictionary<string, string?>
                {
                    ["DataProtection:CertificatePath"] = certificatePath,
                    ["DataProtection:CertificatePassword"] = "test-password",
                }
            );

            // Act
            builder.AddAppFoundation();

            // Assert
            using var provider = builder.Services.BuildServiceProvider();
            var options = provider.GetRequiredService<IOptions<KeyManagementOptions>>().Value;
            Assert.IsType<CertificateXmlEncryptor>(options.XmlEncryptor);
        }
        finally
        {
            File.Delete(certificatePath);
        }
    }

    [Fact]
    public void AddAppFoundation_DefaultDiscriminator_UsesWolverineServiceName()
    {
        // Arrange
        var builder = CreateBuilder();

        // Act
        builder.AddAppFoundation(options => options.WolverineServiceName = "MyApp");

        // Assert
        using var provider = builder.Services.BuildServiceProvider();
        var options = provider.GetRequiredService<IOptions<DataProtectionOptions>>().Value;
        Assert.Equal("MyApp", options.ApplicationDiscriminator);
    }

    [Fact]
    public void AddAppFoundation_ExplicitDiscriminator_OverridesWolverineServiceName()
    {
        // Arrange
        var builder = CreateBuilder();

        // Act
        builder.AddAppFoundation(options =>
        {
            options.WolverineServiceName = "MyApp";
            options.DataProtectionApplicationDiscriminator = "MyDiscriminator";
        });

        // Assert
        using var provider = builder.Services.BuildServiceProvider();
        var options = provider.GetRequiredService<IOptions<DataProtectionOptions>>().Value;
        Assert.Equal("MyDiscriminator", options.ApplicationDiscriminator);
    }

    [Fact]
    public void AddAppFoundation_ConfigureDataProtectionCallback_IsInvoked()
    {
        // Arrange
        var builder = CreateBuilder();
        var invoked = false;

        // Act
        builder.AddAppFoundation(options => options.ConfigureDataProtection = _ => invoked = true);

        // Assert
        Assert.True(invoked);
    }

    [Fact]
    public void AddAppFoundation_ConfigureWolverineCallback_IsInvokedInsideUseWolverine()
    {
        // Arrange — the Wolverine callback is deferred to host build, so build the host.
        var builder = CreateBuilder();
        var invoked = false;
        builder.AddAppFoundation(options => options.ConfigureWolverine = _ => invoked = true);

        // Act
        using var app = builder.Build();

        // Assert
        Assert.True(invoked);
    }

    private static WebApplicationBuilder CreateBuilder(
        IDictionary<string, string?>? extraConfig = null
    )
    {
        var builder = WebApplication.CreateBuilder();
        var config = new Dictionary<string, string?>
        {
            ["ConnectionStrings:appfoundation-database"] =
                "Host=localhost;Port=5432;Database=test;Username=u;Password=p",
        };
        foreach (var pair in extraConfig ?? new Dictionary<string, string?>())
        {
            config[pair.Key] = pair.Value;
        }
        builder.Configuration.AddInMemoryCollection(config);
        return builder;
    }

    private static string WriteSelfSignedPfx(string password)
    {
        using var rsa = RSA.Create(2048);
        var request = new CertificateRequest(
            "CN=AppFoundation Tests",
            rsa,
            HashAlgorithmName.SHA256,
            RSASignaturePadding.Pkcs1
        );
        using var certificate = request.CreateSelfSigned(
            DateTimeOffset.UtcNow.AddDays(-1),
            DateTimeOffset.UtcNow.AddYears(1)
        );

        var path = Path.Combine(Path.GetTempPath(), $"appfoundation-test-{Guid.NewGuid():N}.pfx");
        File.WriteAllBytes(path, certificate.Export(X509ContentType.Pfx, password));
        return path;
    }
}
