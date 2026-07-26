using AndreGoepel.AppFoundation.Aspire;

var builder = DistributedApplication.CreateBuilder(args);

// The E2E suite passes E2E=true to skip the data volume: tests need a throwaway database, and
// sharing the developer's volume would leak their local admin account into the test run.
var isE2E = string.Equals(builder.Configuration["E2E"], "true", StringComparison.OrdinalIgnoreCase);

// The database resource name is the connection-string name the foundation reads by default
// (AppFoundationOptions.DatabaseConnectionName == "appfoundation-database").
var (_, database) = builder.AddStandardPostgres(
    isE2E,
    databaseResourceName: "appfoundation-database",
    databaseName: "appfoundation"
);

// MailHog captures outgoing development email locally: an SMTP server on 1025 and a web UI on
// 8025 to read what was "sent". Nothing leaves the machine, and no real mail account is needed.
var mailhog = builder.AddStandardMailHog();

// The sample web app, wired to the database and started only once it is ready. Email settings
// are database-only (no configuration fallback) — the E2E fixture configures MailHog through the
// real Email Settings admin page itself, the same way a real administrator would.
builder
    .AddProject<Projects.AndreGoepel_AppFoundation_Sample>("web")
    .WithReference(database)
    .WaitFor(database)
    .WaitFor(mailhog);

builder.Build().Run();
