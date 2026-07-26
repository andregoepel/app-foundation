using AndreGoepel.Testing.E2E;
using Aspire.Hosting.ApplicationModel;
using Aspire.Hosting.Testing;

namespace AndreGoepel.AppFoundation.E2ETests.Infrastructure;

/// <summary>
/// AppFoundation's E2E fixture: boots the sample AppHost (Postgres + MailHog + the sample web
/// app) via the shared <see cref="E2EAppFixture"/>, then adds the one thing that's genuinely
/// app-specific — configuring MailHog through the real Email Settings admin page, since email
/// settings are database-only (no configuration fallback) and every E2E run starts from an
/// empty database.
/// </summary>
public sealed class AppFoundationE2EAppFixture : E2EAppFixture
{
    private readonly BuilderCapture _capture;

    public AppFoundationE2EAppFixture()
        : this(new BuilderCapture()) { }

    private AppFoundationE2EAppFixture(BuilderCapture capture)
        : base(
            new E2EAppFixtureOptions
            {
                CreateAppHostBuilder = async args =>
                {
                    var builder =
                        await DistributedApplicationTestingBuilder.CreateAsync<Projects.AndreGoepel_AppFoundation_AppHost>(
                            args
                        );
                    // The base fixture keeps the DistributedApplication it builds from this
                    // private, so EnsureEmailConfiguredAsync below has no way to read MailHog's
                    // *SMTP* endpoint (only its HTTP one, via Mail) unless this fixture captures
                    // the builder itself here. Aspire mutates the same resource-model objects in
                    // place as the app starts, so reading the endpoint through this captured
                    // reference after E2EAppFixture.InitializeAsync has run below returns the
                    // real allocated port — not a guess.
                    capture.Builder = builder;
                    return builder;
                },
                WebResourceName = "web",
                ProvisionAdminButtonText = "Create admin",
                MailHogResourceName = "mailhog",
            }
        )
    {
        _capture = capture;
    }

    private bool _emailConfigured;
    private readonly SemaphoreSlim _emailConfigGate = new(1, 1);

    /// <summary>
    /// Saves MailHog's connection details on the real Email Settings admin page, exactly once per
    /// app instance — the same way a real administrator would. Necessary because email settings
    /// are database-only (no configuration fallback) and every E2E run starts from an empty
    /// database, so nothing would be able to send mail otherwise.
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

            var smtpEndpoint = GetMailHogSmtpEndpoint();

            await using var context = await NewContextAsync();
            var page = await context.NewPageAsync();
            await page.LoginAsync(TestData.AdminEmail, TestData.DefaultPassword);

            await page.GotoAsync("/Administration/EmailSettings");
            await page.FillFieldAsync("SenderName", "AppFoundation E2E");
            await page.FillFieldAsync("SenderEmail", "e2e@appfoundation.local");
            await page.FillFieldAsync("Server", smtpEndpoint.Host);
            await page.FillFieldAsync("Port", smtpEndpoint.Port.ToString());
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

    /// <summary>
    /// Resolves the running MailHog container's actual <c>smtp</c> endpoint — allocated
    /// dynamically by Aspire's testing host even though <c>AddStandardMailHog</c> passes an
    /// explicit host port, so it cannot be hardcoded.
    /// </summary>
    private EndpointReference GetMailHogSmtpEndpoint()
    {
        var builder =
            _capture.Builder
            ?? throw new InvalidOperationException(
                "The AppHost builder was not captured — InitializeAsync must run before this is called."
            );

        if (!builder.Resources.TryGetByName("mailhog", out var mailHog))
        {
            throw new InvalidOperationException(
                "AppHost.cs has no resource named 'mailhog' — check AddStandardMailHog's resource name."
            );
        }

        return ((IResourceWithEndpoints)mailHog).GetEndpoint("smtp");
    }

    /// <summary>
    /// Typed access to the MailHog client this fixture configures via
    /// <see cref="E2EAppFixtureOptions.MailHogResourceName"/>. <see cref="E2EAppFixture.Mail"/> is
    /// deliberately typed as the narrower <see cref="IEmailLinkSource"/> contract, which doesn't
    /// expose <see cref="MailHogClient.ClearAsync"/> — this is the one cast site instead of one
    /// per call.
    /// </summary>
    public Task ClearMailAsync(CancellationToken cancellationToken = default) =>
        ((MailHogClient)Mail!).ClearAsync(cancellationToken);

    /// <summary>
    /// Carries the <see cref="IDistributedApplicationTestingBuilder"/> from the constructor's
    /// <c>CreateAppHostBuilder</c> closure through to <see cref="GetMailHogSmtpEndpoint"/> — a
    /// separate object rather than an instance field on the fixture itself because the closure
    /// has to be built before <c>base(...)</c> runs, when <c>this</c> isn't available yet.
    /// </summary>
    private sealed class BuilderCapture
    {
        public IDistributedApplicationTestingBuilder? Builder { get; set; }
    }
}

[CollectionDefinition(E2ECollectionDefaults.Name)]
public sealed class E2ECollection : ICollectionFixture<AppFoundationE2EAppFixture>;
