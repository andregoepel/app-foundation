using AndreGoepel.AppFoundation;
using AndreGoepel.AppFoundation.Hosting;
using AndreGoepel.AppFoundation.Sample.Components;
using AndreGoepel.Marten.Identity.Blazor.Components.Account;

var builder = WebApplication.CreateBuilder(args);

// One call wires the data store, identity, messaging, email, data protection, and the
// shared request pipeline. The connection string named "appfoundation-database" is
// supplied by the Aspire AppHost (or Docker secrets in production).
builder.AddAppFoundation(options =>
    // Self-service registration is off by default (#49); the sample opts in — the same
    // way a real host would — so the E2E suite can exercise the registration flows.
    options.ConfigureIdentity = identity => identity.EnableUserRegistration = true
);

builder.Services.AddRazorComponents().AddInteractiveServerComponents();

// Brand the management shell. A real host would also set LogoPath and, optionally, an
// AdminMenu component to contribute its own administration entries.
builder.Services.Configure<AppFoundationLayoutOptions>(options =>
{
    options.BrandName = "AppFoundation Sample";
    options.Copyright = $"© {DateTime.UtcNow:yyyy} AppFoundation";
    // A self-contained placeholder logo so the sample needs no binary asset. A real host
    // points LogoPath at its own image under wwwroot.
    options.LogoPath =
        "data:image/svg+xml;utf8,"
        + "<svg xmlns='http://www.w3.org/2000/svg' viewBox='0 0 48 48'>"
        + "<rect width='48' height='48' rx='10' fill='%234f46e5'/>"
        + "<text x='24' y='33' font-size='26' text-anchor='middle' fill='white' "
        + "font-family='sans-serif'>A</text></svg>";
});

var app = builder.Build();

app.UseAppFoundation();

app.MapStaticAssets();
app.MapRazorComponents<App>()
    .AddInteractiveServerRenderMode()
    .AddAdditionalAssemblies(
        // The AppFoundation management UI (layout, setup, dashboard, admin pages) …
        typeof(AppFoundationLayoutOptions).Assembly,
        // … and the Marten.Identity account pages (login, register, account management).
        typeof(AndreGoepel.Marten.Identity.Blazor.Initialization).Assembly
    );

app.MapAdditionalIdentityEndpoints();

app.Run();
