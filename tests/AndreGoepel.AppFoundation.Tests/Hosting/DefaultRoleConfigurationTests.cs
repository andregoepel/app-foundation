using AndreGoepel.AppFoundation.Hosting;
using Microsoft.Extensions.Configuration;

namespace AndreGoepel.AppFoundation.Tests.Hosting;

public class DefaultRoleConfigurationTests
{
    [Fact]
    public void Merge_ArrayForm_BindsEachRole()
    {
        // Arrange
        var configuration = BuildConfiguration(
            new()
            {
                ["AppFoundation:DefaultRoles:0:Name"] = "Editor",
                ["AppFoundation:DefaultRoles:0:Deletable"] = "true",
                ["AppFoundation:DefaultRoles:1:Name"] = "Viewer",
                ["AppFoundation:DefaultRoles:1:Deletable"] = "false",
            }
        );
        var options = new AppFoundationOptions();

        // Act
        Initialization.MergeDefaultRolesConfiguration(configuration, options);

        // Assert
        Assert.Equal(
            [new DefaultRole("Editor", true), new DefaultRole("Viewer", false)],
            options.DefaultRoles
        );
    }

    [Fact]
    public void Merge_DeletableOmitted_DefaultsToTrue()
    {
        // Arrange
        var configuration = BuildConfiguration(
            new() { ["AppFoundation:DefaultRoles:0:Name"] = "Editor" }
        );
        var options = new AppFoundationOptions();

        // Act
        Initialization.MergeDefaultRolesConfiguration(configuration, options);

        // Assert
        Assert.Equal([new DefaultRole("Editor", true)], options.DefaultRoles);
    }

    [Fact]
    public void Merge_ConfigAugmentsCode_AndDeduplicatesByName()
    {
        // Arrange — one role set in code, one added (plus a name-duplicate) via config.
        var configuration = BuildConfiguration(
            new()
            {
                ["AppFoundation:DefaultRoles:0:Name"] = "Member",
                ["AppFoundation:DefaultRoles:0:Deletable"] = "false",
                ["AppFoundation:DefaultRoles:1:Name"] = "Viewer",
            }
        );
        var options = new AppFoundationOptions();
        options.DefaultRoles.Add(new DefaultRole("Member"));

        // Act
        Initialization.MergeDefaultRolesConfiguration(configuration, options);

        // Assert — code entry kept as-is (config does not override it), new one appended.
        Assert.Equal([new DefaultRole("Member"), new DefaultRole("Viewer")], options.DefaultRoles);
    }

    [Fact]
    public void Merge_NoConfiguration_LeavesOptionsUnchanged()
    {
        // Arrange
        var configuration = BuildConfiguration([]);
        var options = new AppFoundationOptions();

        // Act
        Initialization.MergeDefaultRolesConfiguration(configuration, options);

        // Assert
        Assert.Empty(options.DefaultRoles);
    }

    private static IConfiguration BuildConfiguration(Dictionary<string, string?> values) =>
        new ConfigurationBuilder().AddInMemoryCollection(values).Build();
}
