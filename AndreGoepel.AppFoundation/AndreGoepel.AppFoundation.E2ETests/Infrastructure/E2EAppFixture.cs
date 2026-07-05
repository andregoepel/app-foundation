using Aspire.Hosting;
using Aspire.Hosting.ApplicationModel;
using Aspire.Hosting.Testing;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Diagnostics.HealthChecks;

namespace AndreGoepel.AppFoundation.E2ETests.Infrastructure;

#region Fixture

/// <summary>
/// Boots the whole Aspire application graph (Postgres + MailHog + the Blazor web app) once for the
/// entire test collection, then launches a single Chromium browser that every test shares. The app
/// runs in <c>AppHost:TestRun</c> mode so containers are ephemeral and ports are dynamic, keeping
/// each CI run isolated from a developer's persistent local Aspire session.
/// </summary>
public sealed class E2EAppFixture : IAsyncLifetime
{
    private DistributedApplication? _app;
    private IPlaywright? _playwright;
    private IBrowser? _browser;

    /// <summary>Base address of the Blazor web app, e.g. <c>https://localhost:1234/</c>.</summary>
    public string AppBaseUrl { get; private set; } = default!;

    /// <summary>Base address of the MailHog HTTP API used to read delivered emails.</summary>
    public string MailHogApiUrl { get; private set; } = default!;

    /// <summary>The single browser shared by the collection. Tests create their own context.</summary>
    public IBrowser Browser =>
        _browser ?? throw new InvalidOperationException("Browser not started.");

    public MailHogClient MailHog { get; private set; } = default!;

    public async ValueTask InitializeAsync()
    {
        var appHostBuilder =
            await DistributedApplicationTestingBuilder.CreateAsync<Projects.AndreGoepel_AppFoundation_AppHost>([
                "AppHost:TestRun=true",
                // The AppHost declares database-password as a secret parameter with no default.
                "Parameters:database-password=e2e-Test-Password-1!",
            ]);

        appHostBuilder.Services.ConfigureHttpClientDefaults(http =>
            http.AddStandardResilienceHandler()
        );

        _app = await appHostBuilder.BuildAsync();

        var startupTimeout = TimeSpan.FromMinutes(5);
        using var startupCts = new CancellationTokenSource(startupTimeout);

        await _app.StartAsync(startupCts.Token);

        var notifications = _app.Services.GetRequiredService<ResourceNotificationService>();
        await notifications.WaitForResourceHealthyAsync(
            "andregoepel-appfoundation",
            startupCts.Token
        );

        AppBaseUrl = _app.GetEndpoint("andregoepel-appfoundation", "https").ToString();
        MailHogApiUrl = _app.GetEndpoint("mailhog", "web").ToString();
        MailHog = new MailHogClient(MailHogApiUrl);

        _playwright = await Playwright.CreateAsync();
        _browser = await _playwright.Chromium.LaunchAsync(
            new BrowserTypeLaunchOptions
            {
                Headless = !IsHeaded,
                // Virtual authenticator (passkeys) requires Chromium DevTools access; nothing extra needed here.
            }
        );
    }

    private bool _adminProvisioned;
    private readonly SemaphoreSlim _provisionGate = new(1, 1);

    /// <summary>
    /// Runs the one-time Setup flow (create root admin + default roles) exactly once per app instance,
    /// in its own throwaway context so it never disturbs a test's session. Idempotent: if the admin
    /// already exists, Setup server-redirects away and nothing is created.
    /// </summary>
    public async Task ProvisionAdminAsync()
    {
        if (_adminProvisioned)
        {
            return;
        }

        await _provisionGate.WaitAsync();
        try
        {
            if (_adminProvisioned)
            {
                return;
            }

            await using var context = await NewContextAsync();
            var page = await context.NewPageAsync();
            await page.GotoAsync("/Setup");
            await page.WaitForBlazorAsync();

            if (
                new Uri(page.Url)
                    .AbsolutePath.Trim('/')
                    .Equals("Setup", StringComparison.OrdinalIgnoreCase)
            )
            {
                await page.FillFieldAsync("Email", TestData.AdminEmail);
                await page.FillFieldAsync("Password", TestData.DefaultPassword);
                await page.FillFieldAsync("ConfirmPassword", TestData.DefaultPassword);
                await page.ClickButtonAsync("Create admin");
                await page.WaitForURLAsync(url =>
                    !new Uri(url).AbsolutePath.Contains("Setup", StringComparison.OrdinalIgnoreCase)
                );
            }

            _adminProvisioned = true;
        }
        finally
        {
            _provisionGate.Release();
        }
    }

    /// <summary>Creates an isolated browser context (fresh cookies/storage) for a single test.</summary>
    public Task<IBrowserContext> NewContextAsync() =>
        Browser.NewContextAsync(
            new BrowserNewContextOptions
            {
                BaseURL = AppBaseUrl,
                IgnoreHTTPSErrors = true, // dev cert on the https endpoint
            }
        );

    public async ValueTask DisposeAsync()
    {
        if (_browser is not null)
        {
            await _browser.DisposeAsync();
        }

        _playwright?.Dispose();

        if (_app is not null)
        {
            await _app.DisposeAsync();
        }
    }

    private static bool IsHeaded =>
        string.Equals(
            Environment.GetEnvironmentVariable("E2E_HEADED"),
            "true",
            StringComparison.OrdinalIgnoreCase
        );
}

#endregion

#region Collection

[CollectionDefinition(Name)]
public sealed class E2ECollection : ICollectionFixture<E2EAppFixture>
{
    public const string Name = "e2e";
}

#endregion
