namespace AndreGoepel.Website.Models;

public static class HomeContentDefaults
{
    public const string LangDe = "de";
    public const string LangEn = "en";

    private static HomeContent En =>
        new()
        {
            Navigation = new()
            {
                Links = new()
                {
                    ["problem"] = "Problem",
                    ["services"] = "Services",
                    ["about"] = "About",
                    ["cases"] = "Cases",
                    ["contact"] = "Contact",
                },
                Cta = "Start a conversation",
                LangLabel = "Language",
                ThemeLabel = "Theme",
                ThemeOptions = new()
                {
                    ["light"] = "Light",
                    ["dark"] = "Dark",
                    ["system"] = "System",
                },
            },
            Hero = new()
            {
                Eyebrow = "Senior Software Engineer · Remote, Async-First",
                Title = ["Senior Software Engineering", "from greenfield to legacy rescue."],
                Body =
                    "New builds, modernization, and scaling — across .NET and modern JavaScript. Embedded with your team, or in focused sprints.",
                Meta = "15+ years · EN/DE · based in Europe",
                Cta = "Start a conversation",
                ServicesLabel = "Services",
            },
            Problem = new()
            {
                Kicker = "01 — Problem",
                Title = "You might be looking at:",
                Items =
                [
                    "A new product or internal tool that needs to be built right from the start — not refactored six months later.",
                    "A web stack that should have been modernized two years ago.",
                    "A performance problem that grew quietly with your business.",
                    "A new module that needs to fit into a system landscape older than the team running it.",
                    "An internal team that's strong on .NET but needs senior support on the modern web side — or vice versa.",
                ],
                Footer = "That's the kind of work I take on.",
            },
            Services = new()
            {
                Kicker = "02 — Services",
                Title = "How we can work together",
                Items =
                [
                    new()
                    {
                        Tag = "Often the right entry point",
                        Name = "Modernization Sprint",
                        Meta = "4 – 8 weeks · cost ceiling",
                        Body =
                            "One concrete piece of legacy modernized, one feature built, one component delivered. Sprint-based with a cost ceiling. Predictable for both sides.",
                        Bullets = ["Sprint-based", "Cost ceiling", "Predictable"],
                    },
                    new()
                    {
                        Tag = "Senior capacity on tap",
                        Name = "Embedded Senior",
                        Meta = "10 – 20 h / week · 3 month minimum",
                        Body =
                            "Code, reviews, architecture sparring, mentoring — alongside your team. Right when you need senior capacity on tap, not a one-off.",
                        Bullets = ["Monthly retainer", "In your team", "Reviews & mentoring"],
                    },
                    new()
                    {
                        Tag = "Full ownership, real delivery",
                        Name = "Greenfield Module",
                        Meta = "Sprint by sprint · re-planning between",
                        Body =
                            "A complete new system or module, built sprint by sprint with cost ceilings and re-planning between sprints. No fixed-price gambles — fair pricing, full ownership, real delivery.",
                        Bullets = ["Sprint by sprint", "Re-planning", "No fixed-price gambles"],
                    },
                ],
            },
            About = new()
            {
                Kicker = "03 — About",
                Title = "Hi, I'm André.",
                Body =
                [
                    "Senior Software Engineer based in Europe — and usually somewhere on the road, since I work fully remote.",
                    "I've been building web applications professionally for 15+ years, with deep experience across both .NET (C#, ASP.NET, Blazor) and the modern JavaScript stack (TypeScript, Vue, Angular, React, Next.js, Node). That combination is rarer than it should be — and it's usually exactly what's needed when companies want to modernize without throwing away what already works.",
                    "I work in English and German on the same level. Async-first by default, sync time scheduled deliberately. I write code, take responsibility, and speak both engineer and business stakeholder.",
                ],
                Facts =
                [
                    new() { Key = "15+", Value = "years building for the web" },
                    new() { Key = ".NET ↔ JS", Value = "fluent on both sides" },
                    new() { Key = "EN / DE", Value = "native business level" },
                    new() { Key = "Remote", Value = "async-first, deliberate sync" },
                ],
            },
            Cases = new()
            {
                Kicker = "04 — Cases",
                Title = "Selected work",
                Note = "Anonymized engagements. Three real projects, told in summary.",
                Demo = "Anonymized — real engagements.",
                Items =
                [
                    new()
                    {
                        Sector = "Insurance Tech · ~3-person team · DACH · 20 months",
                        Title = "Extending a live broker portal — responsive from day one",
                        Body =
                            "A production broker portal handling the full workflow — contract signing to carrier submission — needed continuous extension without disrupting live ops. Dedicated Node.js API alongside the existing Typo3 stack, VueJS frontend with full responsive coverage.",
                        Result =
                            "Support chat in 2 months · multiple new carrier interfaces · 20 months without rewrite cycles.",
                        Stack = ["Typo3", "VueJS", "Node.js", "REST"],
                    },
                    new()
                    {
                        Sector = "Automotive-adjacent IT · Enterprise scale · Germany",
                        Title = "Undoing microservices on a live enterprise platform",
                        Body =
                            "An IT asset management platform (500k–1M assets per install) had been split into microservices early on; the independent scalability never materialized. Consolidated back into a modular monolith and redesigned ingestion from OPC UA polling to queue-based processing.",
                        Result =
                            "Both corrections shipped without service interruption · ingestion stable almost immediately · 3 enterprise installs (on-prem & AWS).",
                        Stack =
                        [
                            "ASP.NET Core",
                            "C#",
                            "TypeScript",
                            "VueJS",
                            "Docker",
                            "SQL Server",
                            "Postgres",
                        ],
                    },
                    new()
                    {
                        Sector = "Insurance Tech · DACH market · 22 months",
                        Title = "20–30 insurance carriers, one integration layer",
                        Body =
                            "Retrieve documents and data from 20–30 insurance carriers via BiPRO interfaces, then expose them through a normalized REST API and broker portal. Every carrier implements the standard differently — the complexity lives in the gap between spec and reality.",
                        Result =
                            "20–30 carriers integrated · 2–10 hours per carrier after pattern analysis · broker self-service onboarding in 15 dev days.",
                        Stack = ["C#", "ASP.NET WebAPI", "WCF (SOAP)", "VueJS", "SQL Server"],
                    },
                ],
            },
            Contact = new()
            {
                Kicker = "05 — Contact",
                Title = "Got a project in mind?",
                Body =
                    "The fastest way to start: a 60-minute call to talk through where you are and what would help. No commitment, no slide decks.",
                Cta = "Schedule a call",
                Or = "or write to",
                LinkedIn = "LinkedIn",
                LinkedInUrl = "https://linkedin.com/in/andre-goepel",
                GitHub = "GitHub",
                GitHubUrl = "https://github.com/andregoepel",
            },
            Footer = new()
            {
                Note = "© 2026 André Goepel · andregoepel.dev",
                Built = "Crafted with care. Lighthouse ≥ 95.",
            },
        };

    private static HomeContent De =>
        new()
        {
            Navigation = new()
            {
                Links = new()
                {
                    ["problem"] = "Problem",
                    ["services"] = "Leistungen",
                    ["about"] = "Über",
                    ["cases"] = "Cases",
                    ["contact"] = "Kontakt",
                },
                Cta = "Gespräch anfragen",
                LangLabel = "Sprache",
                ThemeLabel = "Theme",
                ThemeOptions = new()
                {
                    ["light"] = "Hell",
                    ["dark"] = "Dunkel",
                    ["system"] = "System",
                },
            },
            Hero = new()
            {
                Eyebrow = "Senior Software Engineer · Remote, Async-First",
                Title = ["Senior Software Engineering", "von Greenfield bis Legacy-Sanierung."],
                Body =
                    "Neubau, Modernisierung, Skalierung — zwischen .NET und modernem JavaScript. Eingebettet im Team oder in fokussierten Sprints.",
                Meta = "15+ Jahre · DE/EN · Basis in Europa",
                Cta = "Gespräch anfragen",
                ServicesLabel = "Leistungen",
            },
            Problem = new()
            {
                Kicker = "01 — Problem",
                Title = "Was gerade auf deinem Tisch liegen könnte:",
                Items =
                [
                    "Ein neues Produkt oder internes Tool, das von Anfang an richtig gebaut werden soll — und nicht sechs Monate später refaktoriert.",
                    "Ein Web-Stack, der schon vor zwei Jahren modernisiert gehört hätte.",
                    "Ein Performance-Problem, das mit dem Geschäft gewachsen ist.",
                    "Ein neues Modul, das in eine Systemlandschaft passen muss, die älter ist als das Team, das sie betreibt.",
                    "Ein internes Team, das .NET stark beherrscht, aber Senior-Support auf der modernen Web-Seite braucht — oder andersherum.",
                ],
                Footer = "Genau diese Art Arbeit übernehme ich.",
            },
            Services = new()
            {
                Kicker = "02 — Leistungen",
                Title = "Wie wir zusammenarbeiten können",
                Items =
                [
                    new()
                    {
                        Tag = "Häufig der richtige Einstieg",
                        Name = "Modernisierungs-Sprint",
                        Meta = "4 – 8 Wochen · Kostendeckel",
                        Body =
                            "Ein konkretes Stück Legacy modernisiert, ein Feature gebaut, eine Komponente geliefert. Sprint-basiert mit Kostendeckel. Planbar für beide Seiten.",
                        Bullets = ["Sprint-basiert", "Kostendeckel", "Planbar"],
                    },
                    new()
                    {
                        Tag = "Senior-Kapazität auf Abruf",
                        Name = "Embedded Senior",
                        Meta = "10 – 20 h / Woche · ab 3 Monate",
                        Body =
                            "Code, Reviews, Architektur-Sparring, Mentoring — direkt im Team. Richtig, wenn du Senior-Kapazität auf Abruf brauchst, nicht einen einmaligen Termin.",
                        Bullets = ["Monats-Pauschale", "Im Team", "Reviews & Mentoring"],
                    },
                    new()
                    {
                        Tag = "Volle Verantwortung, echte Lieferung",
                        Name = "Greenfield-Modul",
                        Meta = "Sprint für Sprint · Re-Planning",
                        Body =
                            "Ein komplett neues System oder Modul, Sprint für Sprint, mit Kostendeckel und Re-Planning zwischen den Sprints. Keine Festpreis-Wetten — faire Bepreisung, volle Verantwortung, echte Lieferung.",
                        Bullets = ["Sprint für Sprint", "Re-Planning", "Keine Festpreis-Wetten"],
                    },
                ],
            },
            About = new()
            {
                Kicker = "03 — Über mich",
                Title = "Hallo, ich bin André.",
                Body =
                [
                    "Senior Software Engineer mit Basis in Europa — und meistens irgendwo unterwegs, weil ich komplett remote arbeite.",
                    "Seit 15+ Jahren baue ich Web-Applikationen — mit Tiefe sowohl in .NET (C#, ASP.NET, Blazor) als auch im modernen JavaScript-Stack (TypeScript, Vue, Angular, React, Next.js, Node). Diese Kombination ist seltener, als sie sein sollte — und sie ist meistens genau das, was Unternehmen brauchen, die modernisieren wollen, ohne wegzuwerfen, was schon funktioniert.",
                    "Ich arbeite gleichermaßen auf Deutsch und Englisch. Async-First als Standard, synchrone Termine bewusst gesetzt. Ich schreibe Code, übernehme Verantwortung — und spreche sowohl Engineer- als auch Geschäftsführer-Sprache.",
                ],
                Facts =
                [
                    new() { Key = "15+", Value = "Jahre Web-Engineering" },
                    new() { Key = ".NET ↔ JS", Value = "auf beiden Seiten zuhause" },
                    new() { Key = "DE / EN", Value = "Business-Niveau" },
                    new() { Key = "Remote", Value = "Async-First, Sync bewusst" },
                ],
            },
            Cases = new()
            {
                Kicker = "04 — Cases",
                Title = "Ausgewählte Arbeit",
                Note = "Anonymisierte Mandate. Drei reale Projekte, in Kurzform.",
                Demo = "Anonymisiert — reale Mandate.",
                Items =
                [
                    new()
                    {
                        Sector = "Insurance Tech · ~3-Personen-Team · DACH · 20 Monate",
                        Title = "Erweiterung eines Live-Portals — responsiv von Anfang an",
                        Body =
                            "Ein produktives Maklerportal mit komplettem Workflow — von der Vertragsunterschrift bis zur Antragsübermittlung — musste laufend erweitert werden, ohne Betriebsunterbrechung. Eigenständiges Node.js-API-Backend neben der bestehenden Typo3-Plattform, VueJS-Frontend mit voller Responsive-Abdeckung.",
                        Result =
                            "Support-Chat in 2 Monaten · mehrere neue Versicherer-Schnittstellen · 20 Monate ohne Rewrite-Zyklen.",
                        Stack = ["Typo3", "VueJS", "Node.js", "REST"],
                    },
                    new()
                    {
                        Sector = "Automotive-nahe IT · Enterprise-Skala · Deutschland",
                        Title =
                            "Microservices rückgebaut — auf einer laufenden Enterprise-Plattform",
                        Body =
                            "Eine IT-Asset-Management-Plattform (500k–1M Assets pro Installation) war früh in Microservices aufgeteilt — die unabhängige Skalierbarkeit stellte sich nie ein. Konsolidiert zu einem modularen Monolithen und Ingestion von OPC-UA-Polling auf Queue-basierte Verarbeitung umgestellt.",
                        Result =
                            "Beide Korrekturen ohne Service-Unterbrechung · Ingestion-Stabilität nahezu sofort · 3 Enterprise-Installationen (on-prem & AWS).",
                        Stack =
                        [
                            "ASP.NET Core",
                            "C#",
                            "TypeScript",
                            "VueJS",
                            "Docker",
                            "SQL Server",
                            "Postgres",
                        ],
                    },
                    new()
                    {
                        Sector = "Insurance Tech · DACH-Markt · 22 Monate",
                        Title = "20–30 Versicherer, eine Integrationsschicht",
                        Body =
                            "Dokumente und Daten aus 20–30 Versicherungsgesellschaften über BiPRO-Schnittstellen abrufen — und über REST-API plus Maklerportal verfügbar machen. Jeder Versicherer implementiert den Standard anders — die Komplexität lebt in der Lücke zwischen Spezifikation und Realität.",
                        Result =
                            "20–30 Versicherer angebunden · 2–10 Stunden pro Versicherer nach Muster-Analyse · Makler-Self-Service-Onboarding in 15 Entwicklungstagen.",
                        Stack = ["C#", "ASP.NET WebAPI", "WCF (SOAP)", "VueJS", "SQL Server"],
                    },
                ],
            },
            Contact = new()
            {
                Kicker = "05 — Kontakt",
                Title = "Du hast ein Projekt im Kopf?",
                Body =
                    "Der schnellste Einstieg: ein 60-minütiges Gespräch, um zu klären, wo du stehst und was helfen würde. Keine Verpflichtung, keine Folien.",
                Cta = "Termin vereinbaren",
                Or = "oder schreib an",
                LinkedIn = "LinkedIn",
                LinkedInUrl = "https://linkedin.com/in/andre-goepel",
                GitHub = "GitHub",
                GitHubUrl = "https://github.com/andregoepel",
            },
            Footer = new()
            {
                Note = "© 2026 André Goepel · andregoepel.dev",
                Built = "Mit Sorgfalt gebaut. Lighthouse ≥ 95.",
            },
        };

    public static HomeContent For(string lang) => lang == LangDe ? De : En;
}
