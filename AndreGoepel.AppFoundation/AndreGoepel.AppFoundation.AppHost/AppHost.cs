var builder = DistributedApplication.CreateBuilder(args);

// When hosted by the E2E test harness (Aspire.Hosting.Testing) this flag is set so that
// containers are ephemeral and ports are assigned dynamically. That keeps every test run
// isolated and avoids clashing with a developer's persistent local Aspire session.
var isTestRun = string.Equals(
    builder.Configuration["AppHost:TestRun"],
    "true",
    StringComparison.OrdinalIgnoreCase
);

var identityDbUser = builder.AddParameter("database-user", "db-user");
var identityDbPassword = builder.AddParameter("database-password", secret: true);

var mailhog = builder
    // Fully-qualified image name so Podman's short-name resolution doesn't prompt/fail; Docker treats it identically.
    .AddContainer("mailhog", "docker.io/mailhog/mailhog")
    .WithEndpoint(targetPort: 1025, name: "smtp", port: isTestRun ? null : 1025)
    .WithHttpEndpoint(targetPort: 8025, name: "web", port: isTestRun ? null : 8025);

var postgresServer = builder.AddPostgres("postgres-server", identityDbUser, identityDbPassword);

if (!isTestRun)
{
    postgresServer.WithLifetime(ContainerLifetime.Persistent).WithHostPort(5432).WithDataVolume();
}

var appFoundationDb = postgresServer.AddDatabase("appfoundation-database");

builder
    .AddProject<Projects.AndreGoepel_AppFoundation>("andregoepel-appfoundation")
    .WithReference(appFoundationDb)
    .WithEnvironment("EmailSender__SenderName", "André Göpel - App Foundation")
    .WithEnvironment("EmailSender__SenderEmail", "no-reply@localhost.dev")
    .WithEnvironment("EmailSender__Username", "test-mail")
    .WithEnvironment("EmailSender__Password", "12345678")
    .WithEnvironment("EmailSender__Port", () => mailhog.GetEndpoint("smtp").Port.ToString())
    .WithEnvironment("EmailSender__Server", () => mailhog.GetEndpoint("smtp").Host)
    .WaitFor(appFoundationDb) // TODO: remove if startup don't need db
    .PublishAsDockerFile();

builder.Build().Run();
