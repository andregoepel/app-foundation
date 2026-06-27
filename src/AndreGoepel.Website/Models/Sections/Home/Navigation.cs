namespace AndreGoepel.Website.Models.Sections.Home;

public class Navigation
{
    public Dictionary<string, string> Links { get; set; } =
        new()
        {
            ["problem"] = "Problem",
            ["services"] = "Services",
            ["about"] = "About",
            ["cases"] = "Cases",
            ["contact"] = "Contact",
        };
    public string Cta { get; set; } = "";
    public string LangLabel { get; set; } = "";
    public string ThemeLabel { get; set; } = "";
    public Dictionary<string, string> ThemeOptions { get; set; } =
        new()
        {
            ["light"] = "Light",
            ["dark"] = "Dark",
            ["system"] = "System",
        };
}
