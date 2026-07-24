using Microsoft.Extensions.Configuration;

var builder = DistributedApplication.CreateBuilder(args);

// A PostgreSQL container with a persistent volume so setup and accounts survive restarts.
// The E2E suite passes E2E=true to skip the volume: tests need a throwaway database, and
// sharing the developer's volume would leak their local admin account into the test run.
var postgres = builder.AddPostgres("postgres");
if (!builder.Configuration.GetValue<bool>("E2E"))
{
    postgres.WithDataVolume();
}

// The database resource name is the connection-string name the foundation reads by default
// (AppFoundationOptions.DatabaseConnectionName == "appfoundation-database").
var database = postgres.AddDatabase("appfoundation-database", "appfoundation");

// MailHog captures outgoing development email locally: an SMTP server on 1025 and a web UI on
// 8025 to read what was "sent". Nothing leaves the machine, and no real mail account is needed.
var mailhog = builder
    .AddContainer("mailhog", "mailhog/mailhog", "v1.0.1")
    .WithEndpoint(name: "smtp", port: 1025, targetPort: 1025)
    .WithHttpEndpoint(name: "http", port: 8025, targetPort: 8025);

// The sample web app, wired to the database and started only once it is ready. Email settings
// are database-only (no configuration fallback) — the E2E fixture configures MailHog through the
// real Email Settings admin page itself, the same way a real administrator would.
builder
    .AddProject<Projects.AndreGoepel_AppFoundation_Sample>("web")
    .WithReference(database)
    .WaitFor(database)
    .WaitFor(mailhog);

builder.Build().Run();
