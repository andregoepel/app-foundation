var builder = DistributedApplication.CreateBuilder(args);

// A PostgreSQL container with a persistent volume so setup and accounts survive restarts.
var postgres = builder.AddPostgres("postgres").WithDataVolume();

// The database resource name is the connection-string name the foundation reads by default
// (AppFoundationOptions.DatabaseConnectionName == "appfoundation-database").
var database = postgres.AddDatabase("appfoundation-database", "appfoundation");

// MailHog captures outgoing development email locally: an SMTP server on 1025 and a web UI on
// 8025 to read what was "sent". Nothing leaves the machine, and no real mail account is needed.
var mailhog = builder
    .AddContainer("mailhog", "mailhog/mailhog", "v1.0.1")
    .WithEndpoint(name: "smtp", port: 1025, targetPort: 1025)
    .WithHttpEndpoint(name: "http", port: 8025, targetPort: 8025);

// The sample web app, wired to the database and started only once it is ready.
builder
    .AddProject<Projects.AndreGoepel_AppFoundation_Sample>("web")
    .WithReference(database)
    .WaitFor(database)
    .WaitFor(mailhog)
    // Pre-configure the EmailSender bootstrap section to send through MailHog. These values
    // come from configuration rather than the database, so email keeps working after a
    // database reset with no manual setup on the Email Settings page. MailHog needs no
    // credentials, but the settings are required, so placeholders are supplied.
    .WithEnvironment("EmailSender__SenderName", "AppFoundation Dev")
    .WithEnvironment("EmailSender__SenderEmail", "dev@appfoundation.local")
    .WithEnvironment("EmailSender__Server", "localhost")
    .WithEnvironment("EmailSender__Port", "1025")
    .WithEnvironment("EmailSender__UseSsl", "false")
    .WithEnvironment("EmailSender__Username", "dev")
    .WithEnvironment("EmailSender__Password", "dev");

builder.Build().Run();
