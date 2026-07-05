using AndreGoepel.AppFoundation;

namespace AndreGoepel.AppFoundation.Tests;

public class AppFoundationLayoutOptionsTests
{
    private sealed class SampleMenu { }

    [Fact]
    public void AdminMenu_IsBackwardCompatibleAliasFor_Menu()
    {
#pragma warning disable CS0618 // exercising the obsolete alias on purpose
        // Arrange / Act — setting the obsolete property flows to Menu.
        var options = new AppFoundationLayoutOptions { AdminMenu = typeof(SampleMenu) };

        // Assert
        Assert.Equal(typeof(SampleMenu), options.Menu);

        // Act — and reading it reflects Menu.
        options.Menu = typeof(string);

        // Assert
        Assert.Equal(typeof(string), options.AdminMenu);
#pragma warning restore CS0618
    }
}
