using AndreGoepel.AppFoundation.Components;
using AndreGoepel.AppFoundation.Hosting;
using AndreGoepel.Marten.Identity.Blazor.Components.Account;
using AndreGoepel.Website;

var builder = WebApplication.CreateBuilder(args);

builder.AddAppFoundation();

builder.Services.AddRazorComponents().AddInteractiveServerComponents();

builder.Services.AddWebsite();

var app = builder.Build();

app.UseAppFoundation();

app.MapStaticAssets();
app.MapRazorComponents<App>()
    .AddInteractiveServerRenderMode()
    .AddAdditionalAssemblies(
        typeof(AndreGoepel.Marten.Identity.Blazor.Initialization).Assembly,
        typeof(AndreGoepel.Website.Initialization).Assembly
    );

app.MapAdditionalIdentityEndpoints();

app.Run();
