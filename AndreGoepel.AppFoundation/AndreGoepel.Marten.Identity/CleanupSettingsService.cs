using Marten;
using Microsoft.Extensions.Options;
using Quartz;

namespace AndreGoepel.Marten.Identity;

public sealed class CleanupSettingsService(
    IDocumentStore documentStore,
    IOptions<DeletedUserCleanupOptions> defaultOptions,
    ISchedulerFactory schedulerFactory
)
{
    private static readonly TriggerKey _triggerKey = new(
        "DeletedUserCleanupTrigger",
        "MartenIdentity"
    );
    private static readonly JobKey _jobKey = new("DeletedUserCleanup", "MartenIdentity");

    public async Task<CleanupSettings> GetAsync(CancellationToken ct = default)
    {
        using var session = documentStore.QuerySession();
        return await session.LoadAsync<CleanupSettings>(CleanupSettings.DocumentId, ct)
            ?? new CleanupSettings
            {
                RetentionDays = defaultOptions.Value.RetentionDays,
                CronSchedule = defaultOptions.Value.CronSchedule,
            };
    }

    public async Task<DateTimeOffset?> GetNextFireTimeAsync(CancellationToken ct = default)
    {
        var scheduler = await schedulerFactory.GetScheduler(ct);
        var trigger = await scheduler.GetTrigger(_triggerKey, ct);
        return trigger?.GetNextFireTimeUtc();
    }

    public async Task SaveAsync(CleanupSettings settings, CancellationToken ct = default)
    {
        using var session = documentStore.LightweightSession();
        session.Store(settings);
        await session.SaveChangesAsync(ct);

        var newTrigger = TriggerBuilder
            .Create()
            .WithIdentity(_triggerKey)
            .ForJob(_jobKey)
            .WithCronSchedule(settings.CronSchedule)
            .Build();

        var scheduler = await schedulerFactory.GetScheduler(ct);
        await scheduler.RescheduleJob(_triggerKey, newTrigger, ct);
    }
}
