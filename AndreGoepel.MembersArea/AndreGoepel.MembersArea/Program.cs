using AndreGoepel.Marten.Identity;
using AndreGoepel.Marten.Identity.Stores;
using AndreGoepel.Marten.Identity.Users;
using AndreGoepel.MembersArea.Components;
using AndreGoepel.MembersArea.Components.Account;
using AndreGoepel.MembersArea.MailService;
using JasperFx;
using Marten;
using Microsoft.AspNetCore.Components.Authorization;
using Microsoft.AspNetCore.Identity;
using Wolverine;

var builder = WebApplication.CreateBuilder(args);

builder.AddServiceDefaults();

// Add services to the container.
builder.Services.AddRazorComponents().AddInteractiveServerComponents();

builder.Services.AddCascadingAuthenticationState();
builder.Services.AddScoped<IdentityRedirectManager>();
builder.Services.AddScoped<
    AuthenticationStateProvider,
    IdentityRevalidatingAuthenticationStateProvider
>();

builder
    .Services.AddAuthentication(options =>
    {
        options.DefaultScheme = IdentityConstants.ApplicationScheme;
        options.DefaultSignInScheme = IdentityConstants.ExternalScheme;
    })
    .AddIdentityCookies();

var connectionString =
    builder.Configuration.GetConnectionString("members-area-database")
    ?? throw new InvalidOperationException("Connection string 'identity-database' not found.");

builder
    .Services.AddIdentityCore<User>(options =>
    {
        options.SignIn.RequireConfirmedAccount = true;
        options.Stores.SchemaVersion = IdentitySchemaVersions.Version3;
    })
    .AddRoles<IdentityRole>()
    .AddUserManager<UserManager<User>>()
    .AddUserStore<UserStore<User>>()
    .AddRoleManager<RoleManager<IdentityRole>>()
    .AddRoleStore<RoleStore<IdentityRole>>()
    .AddDefaultTokenProviders()
    .AddSignInManager()
    .AddDefaultTokenProviders();

builder.Services.AddScoped<IEmailSender<User>, IdentityEmailSender>();

builder.Services.AddMarten(options =>
{
    options.Connection(connectionString);

    options.InitializeIdentity();
    options.AutoCreateSchemaObjects = AutoCreate.All;
});

//.IntegrateWithWolverine();

builder.Host.UseWolverine(options =>
{
    options.ServiceName = "MembersArea";

    options.Discovery.IncludeAssembly(typeof(SendEmailMessageHandler).Assembly);
    //options.PersistMessagesWithMarten();
});

builder.AddEmailService();

builder.Services.AddDataProtection();

var app = builder.Build();

app.MapDefaultEndpoints();

// Configure the HTTP request pipeline.
if (!app.Environment.IsDevelopment())
{
    app.UseExceptionHandler("/Error", createScopeForErrors: true);
    // The default HSTS value is 30 days. You may want to change this for production scenarios, see https://aka.ms/aspnetcore-hsts.
    app.UseHsts();
}
app.UseStatusCodePagesWithReExecute("/not-found", createScopeForStatusCodePages: true);
app.UseHttpsRedirection();

app.UseAntiforgery();

app.MapStaticAssets();
app.MapRazorComponents<App>().AddInteractiveServerRenderMode();

// Add additional endpoints required by the Identity /Account Razor components.
app.MapAdditionalIdentityEndpoints();

app.Run();
