using AndreGoepel.Website.Services;

namespace AndreGoepel.Website.Tests.Services;

public class SiteStateServiceTests
{
    [Fact]
    public void DefaultLang_IsEnglish()
    {
        // Arrange / Act
        var service = new SiteStateService();

        // Assert
        Assert.Equal("en", service.Lang);
    }

    [Fact]
    public void DefaultTheme_IsDark()
    {
        // Arrange / Act
        var service = new SiteStateService();

        // Assert
        Assert.Equal("dark", service.Theme);
        Assert.Equal("dark", service.ResolvedTheme);
    }

    [Fact]
    public void SetLang_DifferentValue_FiresOnChange()
    {
        // Arrange
        var service = new SiteStateService();
        var fired = 0;
        service.OnChange += () => fired++;

        // Act
        service.SetLang("de");

        // Assert
        Assert.Equal("de", service.Lang);
        Assert.Equal(1, fired);
    }

    [Fact]
    public void SetLang_SameValue_DoesNotFireOnChange()
    {
        // Arrange
        var service = new SiteStateService();
        var fired = 0;
        service.OnChange += () => fired++;

        // Act
        service.SetLang("en");

        // Assert
        Assert.Equal(0, fired);
    }

    [Fact]
    public void SetTheme_DifferentValue_FiresOnChange()
    {
        // Arrange
        var service = new SiteStateService();
        var fired = 0;
        service.OnChange += () => fired++;

        // Act
        service.SetTheme("light", "light");

        // Assert
        Assert.Equal("light", service.Theme);
        Assert.Equal("light", service.ResolvedTheme);
        Assert.Equal(1, fired);
    }

    [Fact]
    public void SetTheme_SameValues_DoesNotFireOnChange()
    {
        // Arrange
        var service = new SiteStateService();
        var fired = 0;
        service.OnChange += () => fired++;

        // Act
        service.SetTheme("dark", "dark");

        // Assert
        Assert.Equal(0, fired);
    }

    [Fact]
    public void SetTheme_OnlyResolvedThemeChanges_FiresOnChange()
    {
        // Arrange — Theme="system" stays the same, ResolvedTheme flips between dark/light.
        var service = new SiteStateService();
        service.SetTheme("system", "dark");
        var fired = 0;
        service.OnChange += () => fired++;

        // Act
        service.SetTheme("system", "light");

        // Assert
        Assert.Equal("light", service.ResolvedTheme);
        Assert.Equal(1, fired);
    }
}
