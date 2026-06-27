using AndreGoepel.AppFoundation.Hosting;
using Microsoft.AspNetCore.Builder;
using Microsoft.Extensions.Configuration;

namespace AndreGoepel.AppFoundation.Tests.Hosting;

public class AddAppFoundationSecretsTests
{
    [Fact]
    public void AddAppFoundation_ReadsConnectionStringFromSecretsDirectory()
    {
        // Arrange
        var secretsDir = Directory.CreateTempSubdirectory();
        var connectionString = "Host=localhost;Port=5432;Database=test;Username=u;Password=s3cret";
        File.WriteAllText(
            Path.Combine(secretsDir.FullName, "ConnectionStrings__appfoundation-database"),
            connectionString
        );
        var builder = WebApplication.CreateBuilder();

        // Act
        builder.AddAppFoundation(options => options.SecretsDirectory = secretsDir.FullName);

        // Assert
        Assert.Equal(
            connectionString,
            builder.Configuration.GetConnectionString("appfoundation-database")
        );
    }
}
