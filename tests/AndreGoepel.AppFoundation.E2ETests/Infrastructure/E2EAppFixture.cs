using Aspire.Hosting;
using Aspire.Hosting.ApplicationModel;
using Aspire.Hosting.Testing;
using Microsoft.Extensions.DependencyInjection;

namespace AndreGoepel.AppFoundation.E2ETests.Infrastructure;

#region Fixture

/// <summary>
/// Boots the whole Aspire application graph (Postgres + MailHog + the sample web app) once for the
/// entire test collection via the sample AppHost, then launches a single Chromium browser that every
/// test shares. The AppHost is started with E2E=true so Postgres runs without its persistent
/// volume — every suite run gets a throwaway, empty database, locally and on CI alike.
/// </summary>
public sealed class E2EAppFixture : IAsyncLifetime
{
    // Resource names as declared in samples/AndreGoepel.AppFoundation.AppHost/AppHost.cs.
    private const string WebResource = "web";
    private const string MailHogResource = "mailhog";

    private DistributedApplication? _app;
    private IPlaywright? _playwright;
    private IBrowser? _browser;
    private Uri _mailHogSmtpEndpoint = default!;

    /// <summary>Base address of the sample web app, e.g. <c>https://localhost:1234/</c>.</summary>
    public string AppBaseUrl { get; private set; } = default!;

    /// <summary>Base address of the MailHog HTTP API used to read delivered emails.</summary>
    public string MailHogApiUrl { get; private set; } = default!;

    /// <summary>The single browser shared by the collection. Tests create their own context.</summary>
    public IBrowser Browser =>
        _browser ?? throw new InvalidOperationException("Browser not started.");

    public MailHogClient MailHog { get; private set; } = default!;

    public async ValueTask InitializeAsync()
    {
        // E2E=true tells the AppHost to run Postgres without its persistent volume, so every
        // suite run starts from an empty database instead of the developer's local data.
        var appHostBuilder =
            await DistributedApplicationTestingBuilder.CreateAsync<Projects.AndreGoepel_AppFoundation_AppHost>([
                "E2E=true",
            ]);

        _app = await appHostBuilder.BuildAsync();

        var startupTimeout = TimeSpan.FromMinutes(5);
        using var startupCts = new CancellationTokenSource(startupTimeout);

        await _app.StartAsync(startupCts.Token);

        var notifications = _app.Services.GetRequiredService<ResourceNotificationService>();
        await notifications.WaitForResourceHealthyAsync(WebResource, startupCts.Token);

        AppBaseUrl = _app.GetEndpoint(WebResource, "https").ToString();
        MailHogApiUrl = _app.GetEndpoint(MailHogResource, "http").ToString();
        MailHog = new MailHogClient(MailHogApiUrl);
        _mailHogSmtpEndpoint = _app.GetEndpoint(MailHogResource, "smtp");

        _playwright = await Playwright.CreateAsync();
        _browser = await _playwright.Chromium.LaunchAsync(
            new BrowserTypeLaunchOptions { Headless = !IsHeaded }
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
                // On the app's very first circuit, interactivity can attach a beat after
                // window.Blazor exists (cold JIT); a click landing in that gap silently
                // does nothing. Retry fill+submit, reloading in between — once the setup
                // succeeded server-side, the reload redirects away by itself.
                for (var attempt = 0; ; attempt++)
                {
                    await page.FillFieldAsync("Email", TestData.AdminEmail);
                    await page.FillFieldAsync("Password", TestData.DefaultPassword);
                    await page.FillFieldAsync("ConfirmPassword", TestData.DefaultPassword);
                    await page.ClickButtonAsync("Create admin");
                    try
                    {
                        await page.WaitForURLAsync(
                            url =>
                                !new Uri(url).AbsolutePath.Contains(
                                    "Setup",
                                    StringComparison.OrdinalIgnoreCase
                                ),
                            new PageWaitForURLOptions { Timeout = 15_000 }
                        );
                        break;
                    }
                    catch (TimeoutException) when (attempt < 7)
                    {
                        await page.ReloadAsync();
                        await page.WaitForBlazorAsync();
                        if (
                            !new Uri(page.Url)
                                .AbsolutePath.Trim('/')
                                .Equals("Setup", StringComparison.OrdinalIgnoreCase)
                        )
                        {
                            break;
                        }
                    }
                }
            }

            _adminProvisioned = true;
        }
        finally
        {
            _provisionGate.Release();
        }
    }

    private bool _emailConfigured;
    private readonly SemaphoreSlim _emailConfigGate = new(1, 1);

    /// <summary>
    /// Saves MailHog's connection details on the real Email Settings admin page, exactly once per
    /// app instance — the same way a real administrator would. Necessary because email settings are
    /// database-only (no configuration fallback) and every E2E run starts from an empty database, so
    /// nothing would be able to send mail otherwise.
    /// </summary>
    public async Task EnsureEmailConfiguredAsync()
    {
        if (_emailConfigured)
        {
            return;
        }

        await _emailConfigGate.WaitAsync();
        try
        {
            if (_emailConfigured)
            {
                return;
            }

            await ProvisionAdminAsync();

            await using var context = await NewContextAsync();
            var page = await context.NewPageAsync();
            await page.LoginAsync(TestData.AdminEmail, TestData.DefaultPassword);

            await page.GotoAsync("/Administration/EmailSettings");
            await page.FillFieldAsync("SenderName", "AppFoundation E2E");
            await page.FillFieldAsync("SenderEmail", "e2e@appfoundation.local");
            await page.FillFieldAsync("Server", _mailHogSmtpEndpoint.Host);
            await page.FillFieldAsync("Port", _mailHogSmtpEndpoint.Port.ToString());
            await page.FillFieldAsync("Username", "e2e");
            // MailHog needs no credentials, but the field is required on first save.
            await page.FillFieldAsync("Password", "e2e");
            await page.ClickButtonAsync("Save changes");
            await page.WaitForSelectorAsync(
                "text=Saved",
                new PageWaitForSelectorOptions { Timeout = 10_000 }
            );

            _emailConfigured = true;
        }
        finally
        {
            _emailConfigGate.Release();
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
