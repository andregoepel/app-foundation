using Marten;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Quartz;

namespace AndreGoepel.Marten.Identity;

internal sealed class CleanupScheduleStartupService(
    ISchedulerFactory schedulerFactory,
    IDocumentStore documentStore,
    IHostApplicationLifetime lifetime,
    ILogger<CleanupScheduleStartupService> logger
) : IHostedService
{
    private static readonly TriggerKey _triggerKey = new(
        "DeletedUserCleanupTrigger",
        "MartenIdentity"
    );
    private static readonly JobKey _jobKey = new("DeletedUserCleanup", "MartenIdentity");

    public Task StartAsync(CancellationToken cancellationToken)
    {
        lifetime.ApplicationStarted.Register(() => _ = ApplyStoredScheduleAsync());
        return Task.CompletedTask;
    }

    public Task StopAsync(CancellationToken cancellationToken) => Task.CompletedTask;

    private async Task ApplyStoredScheduleAsync()
    {
        try
        {
            using var session = documentStore.QuerySession();
            var settings = await session.LoadAsync<CleanupSettings>(CleanupSettings.DocumentId);
            if (settings is null)
                return;

            var scheduler = await schedulerFactory.GetScheduler();
            var newTrigger = TriggerBuilder
                .Create()
                .WithIdentity(_triggerKey)
                .ForJob(_jobKey)
                .WithCronSchedule(settings.CronSchedule)
                .Build();

            var nextFire = await scheduler.RescheduleJob(_triggerKey, newTrigger);
            if (nextFire.HasValue)
                logger.LogInformation(
                    "Applied stored cleanup schedule '{CronSchedule}'. Next run: {NextFire:u}.",
                    settings.CronSchedule,
                    nextFire.Value
                );
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "Failed to apply stored cleanup schedule on startup.");
        }
    }
}
