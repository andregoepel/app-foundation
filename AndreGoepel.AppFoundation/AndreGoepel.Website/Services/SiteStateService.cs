namespace AndreGoepel.Website.Services;

public class SiteStateService
{
    public string Lang { get; private set; } = "en";
    public string Theme { get; private set; } = "dark";
    public string ResolvedTheme { get; private set; } = "dark";

    public event Action? OnChange;

    public void SetLang(string lang)
    {
        if (Lang == lang)
            return;
        Lang = lang;
        NotifyChange();
    }

    public void SetTheme(string theme, string resolvedTheme)
    {
        var changed = Theme != theme || ResolvedTheme != resolvedTheme;
        Theme = theme;
        ResolvedTheme = resolvedTheme;
        if (changed)
            NotifyChange();
    }

    private void NotifyChange() => OnChange?.Invoke();
}
