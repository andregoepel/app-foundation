namespace AndreGoepel.Marten.Identity;

public sealed class CleanupSettings
{
    public static readonly Guid DocumentId = new("b3e1a7c2-4f5d-6e8a-9b0c-1d2e3f4a5b6c");

    public Guid Id { get; set; } = DocumentId;
    public int RetentionDays { get; set; } = 30;
    public string CronSchedule { get; set; } = "0 0 0 * * ?";
}
