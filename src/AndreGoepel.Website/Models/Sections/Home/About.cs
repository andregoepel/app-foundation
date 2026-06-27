namespace AndreGoepel.Website.Models.Sections.Home;

public class About
{
    public string Kicker { get; set; } = "";
    public string Title { get; set; } = "";
    public List<string> Body { get; set; } = [];
    public List<FactItem> Facts { get; set; } = [];
}
