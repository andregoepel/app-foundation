namespace AndreGoepel.Website.Models;

public class SiteContent
{
    public string Id { get; set; } = "andregoepel.dev:en";
    public string SiteId { get; set; } = "andregoepel.dev";
    public string Lang { get; set; } = "en";
    public HomeContent Content { get; set; } = HomeContentDefaults.For(HomeContentDefaults.LangEn);
}
