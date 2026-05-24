namespace AndreGoepel.Marten.Identity;

public sealed class DeletedUserCleanupOptions
{
    public int RetentionDays { get; set; } = 30;

    /// <summary>Quartz cron expression controlling when the cleanup runs. Defaults to daily at midnight UTC.</summary>
    public string CronSchedule { get; set; } = "0 0 0 * * ?";
}
