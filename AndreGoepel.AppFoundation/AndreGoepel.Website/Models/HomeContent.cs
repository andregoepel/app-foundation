using AndreGoepel.Website.Models.Sections.Home;

namespace AndreGoepel.Website.Models;

public class HomeContent
{
    public required Navigation Navigation { get; init; }
    public required Hero Hero { get; init; }
    public required Problems Problem { get; init; }
    public required Sections.Home.Services Services { get; init; }
    public required About About { get; init; }
    public required Cases Cases { get; init; }
    public required Contact Contact { get; init; }
    public required Footer Footer { get; init; }

    public static HomeContent Create() =>
        new()
        {
            About = new(),
            Cases = new(),
            Contact = new(),
            Footer = new(),
            Hero = new(),
            Navigation = new(),
            Problem = new(),
            Services = new(),
        };
}
