using AndreGoepel.AppFoundation;
using AndreGoepel.AppFoundation.Hosting;
using AndreGoepel.Marten.Identity.Blazor.Components.Account;
using AndreGoepel.Website;
using AndreGoepel.Website.Components;

var builder = WebApplication.CreateBuilder(args);

builder.AddAppFoundation();

builder.Services.AddRazorComponents().AddInteractiveServerComponents();

builder.Services.AddWebsite();

builder.Services.Configure<AppFoundationLayoutOptions>(options =>
{
    options.BrandName = "nerdventures.blog";
    options.Copyright = "nerdventures.blog © 2025";
    options.AdminMenu = typeof(WebsiteAdminMenu);
});

var app = builder.Build();

app.UseAppFoundation();

app.MapStaticAssets();
app.MapRazorComponents<App>()
    .AddInteractiveServerRenderMode()
    .AddAdditionalAssemblies(
        typeof(AppFoundationLayoutOptions).Assembly,
        typeof(AndreGoepel.Marten.Identity.Blazor.Initialization).Assembly
    );

app.MapAdditionalIdentityEndpoints();

app.Run();
