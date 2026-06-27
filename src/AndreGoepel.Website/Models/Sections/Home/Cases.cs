namespace AndreGoepel.Website.Models.Sections.Home;

public class Cases
{
    public string Kicker { get; set; } = "";
    public string Title { get; set; } = "";
    public string Note { get; set; } = "";
    public string Demo { get; set; } = "";
    public List<CaseItem> Items { get; set; } = [];
}
