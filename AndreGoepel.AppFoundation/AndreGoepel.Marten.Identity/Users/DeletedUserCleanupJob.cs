using Marten;
using Microsoft.Extensions.Logging;
using Quartz;

namespace AndreGoepel.Marten.Identity.Users;

[DisallowConcurrentExecution]
internal sealed class DeletedUserCleanupJob(
    IDocumentStore documentStore,
    CleanupSettingsService settingsService,
    ILogger<DeletedUserCleanupJob> logger
) : IJob
{
    public async Task Execute(IJobExecutionContext context)
    {
        try
        {
            var settings = await settingsService.GetAsync(context.CancellationToken);
            var cutoff = DateTimeOffset.UtcNow.AddDays(-settings.RetentionDays);

            using var querySession = documentStore.QuerySession();
            var usersToClean = await querySession
                .Query<User>()
                .Where(u => u.Deleted && u.DeletedAt < cutoff)
                .ToListAsync(context.CancellationToken);

            if (usersToClean.Count == 0)
                return;

            logger.LogInformation(
                "Purging event streams and documents for {Count} deleted user(s) beyond retention period.",
                usersToClean.Count
            );

            using var session = documentStore.LightweightSession();

            foreach (var user in usersToClean)
            {
                session.Events.ArchiveStream(user.StreamId);
                session.Delete(user);
            }

            await session.SaveChangesAsync(context.CancellationToken);
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "An error occurred during deleted-user cleanup.");
        }
    }
}
