namespace AndreGoepel.AppFoundation.Hosting;

/// <summary>
/// Consumer-facing knobs for the AppFoundation backend seam. Defaults match the
/// in-repo host; downstream apps override the connection name and Wolverine
/// service name as needed.
/// </summary>
public sealed class AppFoundationOptions
{
    /// <summary>
    /// Name of the connection string (in configuration) for the Marten/PostgreSQL store.
    /// </summary>
    public string DatabaseConnectionName { get; set; } = "appfoundation-database";

    /// <summary>
    /// Wolverine service name used for the durable inbox/outbox endpoints.
    /// </summary>
    public string WolverineServiceName { get; set; } = "AppFoundation";
}
