using AndreGoepel.AppFoundation;

namespace AndreGoepel.AppFoundation.Tests;

public sealed class AppFoundationLayoutOptionsTests
{
    [Fact]
    public void HomeUrl_DefaultsToThePackagedDashboard()
    {
        // Arrange / Act
        var options = new AppFoundationLayoutOptions();

        // Assert — hosts that keep the default get the packaged dashboard page.
        Assert.Equal("dashboard", options.HomeUrl);
    }
}
