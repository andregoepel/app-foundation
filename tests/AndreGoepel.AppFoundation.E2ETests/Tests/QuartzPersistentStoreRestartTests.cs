using AndreGoepel.AppFoundation.Hosting;
using AndreGoepel.Testing.E2E;
using Aspire.Hosting;
using Aspire.Hosting.ApplicationModel;
using Aspire.Hosting.Testing;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Hosting;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Npgsql;
using Quartz;

namespace AndreGoepel.AppFoundation.E2ETests.Tests;

/// <summary>
/// Verifies the one risk #129's persistent Quartz store couldn't be confirmed from binary
/// inspection alone: that marten-identity's DI-registered cleanup job/trigger
/// (<c>AddMartenIdentityCleanup</c>) re-schedules cleanly against an already-provisioned
/// store on a second app startup, rather than throwing (e.g. <c>ObjectAlreadyExistsException</c>)
/// on every restart after the first. Solved via Aspire rather than a new Testcontainers
/// dependency — but not via a bespoke ad-hoc Postgres-only model:
/// <c>DistributedApplicationTestingBuilder.Create()</c> requires the calling project to
/// itself be an AppHost-SDK project, which this test project isn't. Instead this reuses the
/// exact mechanism <c>AndreGoepel.Testing.E2E</c>'s <see cref="E2EAppFixture"/> already proves
/// works — booting the real sample AppHost — and only uses its Postgres resource's connection
/// string; the "web" and "mailhog" resources that come along for the ride are unused here.
/// <para>
/// Uses its own <see cref="IAsyncLifetime"/> rather than injecting the app's own
/// <c>AndreGoepel.AppFoundation.E2ETests.Infrastructure.AppFoundationE2EAppFixture</c> — it
/// needs its own throwaway app graph, since the shared fixture's Postgres has other tests' data
/// on it by the time any single test runs. Still joins the shared <see cref="E2ECollectionDefaults"/>
/// collection (without consuming its fixture) purely so xUnit runs it sequentially against the
/// rest of the E2E suite instead of in parallel: two full Aspire/Docker/Postgres graphs running
/// concurrently on a CI runner caused unrelated Playwright navigations elsewhere in the suite to
/// flake with network errors under the combined resource pressure.
/// </para>
/// </summary>
[Collection(E2ECollectionDefaults.Name)]
public sealed class QuartzPersistentStoreRestartTests : IAsyncLifetime
{
    private const string DatabaseResourceName = "appfoundation-database";
    private const string WebResourceName = "web";
    private static readonly TriggerKey CleanupTriggerKey = new(
        "DeletedUserCleanupTrigger",
        "MartenIdentity"
    );

    private DistributedApplication? _app;
    private string _connectionString = default!;

    public async ValueTask InitializeAsync()
    {
        // E2E=true: throwaway Postgres (no persistent volume) — see AppFoundationE2EAppFixture.
        var appHostBuilder =
            await DistributedApplicationTestingBuilder.CreateAsync<Projects.AndreGoepel_AppFoundation_AppHost>([
                "E2E=true",
            ]);

        _app = await appHostBuilder.BuildAsync();

        using var startupCts = new CancellationTokenSource(TimeSpan.FromMinutes(5));
        await _app.StartAsync(startupCts.Token);

        // Waiting on "web" (rather than "postgres" directly) reuses the same readiness
        // signal AppFoundationE2EAppFixture already relies on — by the time it's healthy, Postgres is
        // definitely up too, since "web" waits for it.
        var notifications = _app.Services.GetRequiredService<ResourceNotificationService>();
        await notifications.WaitForResourceHealthyAsync(WebResourceName, startupCts.Token);

        _connectionString =
            await _app.GetConnectionStringAsync(DatabaseResourceName, startupCts.Token)
            ?? throw new InvalidOperationException("Postgres resource has no connection string.");
    }

    public async ValueTask DisposeAsync()
    {
        if (_app is not null)
        {
            await _app.DisposeAsync();
        }
    }

    [Fact]
    public async Task SecondStartup_AgainstAlreadyProvisionedStore_DoesNotThrow_AndTriggerStaysScheduled()
    {
        // Arrange / Act — first startup: qrtz_ tables don't exist yet, provisioned here;
        // the DI-registered cleanup job/trigger gets scheduled for the first time. Kept
        // alive (not disposed) until the second app has also started — Quartz.Extensions
        // .Hosting bridges its static, process-wide log provider to whichever app's
        // ILoggerFactory configured it, so disposing the first app early (and with it, its
        // LoggerFactory) crashes the second app's own Quartz activity with an unrelated
        // ObjectDisposedException.
        await using var first = await StartAppAsync();
        await AssertCleanupTriggerIsPersistedAsync();

        // Act — second startup: a fresh app instance (fresh WebApplicationBuilder, fresh DI
        // container) against the same, already-provisioned database and already-persisted
        // trigger. This is the scenario that couldn't be confirmed from binary inspection
        // alone: whether Quartz.Extensions.DependencyInjection's DI-driven job/trigger
        // registration (ContainerConfigurationProcessor) re-schedules cleanly against an
        // existing persisted trigger, or throws (e.g. ObjectAlreadyExistsException) — the
        // real assertion here is that StartAppAsync doesn't throw.
        await using var second = await StartAppAsync();

        // Assert — the persisted trigger row still has a valid next-fire-time after the
        // restart. Checked directly against Postgres rather than through Quartz's own
        // GetTrigger API: that API additionally probes the qrtz_triggers schema to decide
        // between two different SELECT shapes (misfire-recovery support), a delegate-level
        // concern unrelated to what this test verifies and one that behaved inconsistently
        // across the two schedulers in practice.
        await AssertCleanupTriggerIsPersistedAsync();
    }

    private async Task AssertCleanupTriggerIsPersistedAsync()
    {
        await using var connection = new NpgsqlConnection(_connectionString);
        await connection.OpenAsync();
        await using var command = new NpgsqlCommand(
            "SELECT next_fire_time FROM qrtz_triggers WHERE trigger_name = @name AND trigger_group = @group",
            connection
        );
        command.Parameters.AddWithValue("name", CleanupTriggerKey.Name);
        command.Parameters.AddWithValue("group", CleanupTriggerKey.Group);

        Assert.NotNull(await command.ExecuteScalarAsync());
    }

    private async Task<WebApplication> StartAppAsync()
    {
        var builder = WebApplication.CreateBuilder(
            new WebApplicationOptions { EnvironmentName = "Development" }
        );

        // Ephemeral port — avoids collisions with other tests/processes; nothing in this
        // test issues HTTP requests, it only needs hosted services (Quartz, Marten, etc.)
        // to start.
        builder.WebHost.UseUrls("http://127.0.0.1:0");

        builder.Configuration.AddInMemoryCollection(
            new Dictionary<string, string?>
            {
                [$"ConnectionStrings:{DatabaseResourceName}"] = _connectionString,
            }
        );

        builder.AddAppFoundation();

        // AddAppFoundation registers several Blazor-circuit-scoped services (Radzen's
        // DialogService/TooltipService/etc., UserInvitationMailer) that depend on
        // NavigationManager/IJSRuntime — normally supplied by an active Blazor Server
        // circuit. Nothing here ever establishes one, but Development's default
        // ValidateOnBuild/ValidateScopes eagerly checks every scoped registration is
        // constructible regardless, so — exactly like a real host's Program.cs —
        // AddRazorComponents/AddInteractiveServerComponents needs to be registered too, to
        // supply those types.
        builder.Services.AddRazorComponents().AddInteractiveServerComponents();

        var app = builder.Build();
        app.UseAppFoundation();

        using var startCts = new CancellationTokenSource(TimeSpan.FromMinutes(2));
        await app.StartAsync(startCts.Token);

        return app;
    }
}
