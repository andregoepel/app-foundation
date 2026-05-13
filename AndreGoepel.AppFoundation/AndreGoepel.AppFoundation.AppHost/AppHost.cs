var builder = DistributedApplication.CreateBuilder(args);

var identityDbUser = builder.AddParameter("database-user", "db-user");
var identityDbPassword = builder.AddParameter("database-password", secret: true);

var mailhog = builder
    .AddContainer("mailhog", "mailhog/mailhog")
    .WithEndpoint(1025, 1025, name: "smtp")
    .WithHttpEndpoint(8025, 8025, name: "web");

var postgresServer = builder
    .AddPostgres("postgres-server", identityDbUser, identityDbPassword)
    .WithLifetime(ContainerLifetime.Persistent)
    .WithHostPort(5432)
    .WithDataVolume();
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
