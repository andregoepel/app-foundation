using AndreGoepel.AppFoundation.Components;
using AndreGoepel.AppFoundation.Components.Account;
using AndreGoepel.AppFoundation.MailService;
using AndreGoepel.Marten.Identity;
using AndreGoepel.Marten.Identity.Blazor;
using AndreGoepel.Marten.Identity.Blazor.Components.Account;
using AndreGoepel.Marten.Identity.Users;
using JasperFx;
using Marten;
using Microsoft.AspNetCore.Identity;
using Radzen;
using Wolverine;
using Wolverine.Marten;

var builder = WebApplication.CreateBuilder(args);

builder.AddServiceDefaults();

builder.Services.AddRazorComponents().AddInteractiveServerComponents();

builder.Services.AddMartenIdentity();
builder.Services.AddMartenIdentityBlazor();

var connectionString =
    builder.Configuration.GetConnectionString("appfoundation-database")
    ?? throw new InvalidOperationException("Connection string 'appfoundation-database' not found.");

builder.Services.AddScoped<IEmailSender<User>, IdentityEmailSender>();

builder
    .Services.AddMarten(options =>
    {
        options.Connection(connectionString);

        options.InitializeIdentity();
        options.AutoCreateSchemaObjects = AutoCreate.All;
    })
    .IntegrateWithWolverine();

builder.Services.AddScoped<NotificationService>();

builder.Host.UseWolverine(options =>
{
    options.ServiceName = "AppFoundation";

    options.Policies.UseDurableInboxOnAllListeners();
    options.Policies.UseDurableOutboxOnAllSendingEndpoints();

    options.Discovery.IncludeAssembly(typeof(SendEmailMessageHandler).Assembly);
});

builder.AddEmailService();

builder.Services.AddDataProtection();

builder.Services.AddRadzenComponents();

builder.Services.AddHeaderPropagation();

var app = builder.Build();

app.MapDefaultEndpoints();

if (!app.Environment.IsDevelopment())
{
    app.UseExceptionHandler("/Error", createScopeForErrors: true);
    app.UseHsts();
}
app.UseHttpsRedirection();
app.UseStaticFiles();
app.UseHeaderPropagation();
app.UseAntiforgery();
app.UseAuthentication();
app.UseAuthorization();

app.UseMartenIdentityMiddleware();

app.MapStaticAssets();
app.MapRazorComponents<App>()
    .AddInteractiveServerRenderMode()
    .AddAdditionalAssemblies(typeof(AndreGoepel.Marten.Identity.Blazor.Initialization).Assembly);

app.MapAdditionalIdentityEndpoints();

app.Run();
