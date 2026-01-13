using Aspire.Hosting;

var builder = DistributedApplication.CreateBuilder(args);

var identityDbUser = builder.AddParameter("database-user", "db-user");
var identityDbPassword = builder.AddParameter("database-password", "Password1+", true);

var mailhog = builder
    .AddContainer("mailhog", "mailhog/mailhog")
    .WithEndpoint(1025, 1025, name: "smtp")
    .WithHttpEndpoint(8025, 8025, name: "web");

var postgresServer = builder
    .AddPostgres("postgres-server", identityDbUser, identityDbPassword)
    .WithLifetime(ContainerLifetime.Persistent)
    .WithHostPort(5432)
    .WithDataVolume();
var membersAreaDb = postgresServer.AddDatabase("members-area-database");

builder
    .AddProject<Projects.AndreGoepel_MembersArea>("andregoepel-membersarea")
    .WithReference(membersAreaDb)
    .WithEnvironment("EmailSender__SenderName", "André Göpel - Member Area")
    .WithEnvironment("EmailSender__SenderEmail", "no-reply@localhost.dev")
    .WithEnvironment("EmailSender__Username", "test-mail")
    .WithEnvironment("EmailSender__Password", "12345678")
    .WithEnvironment("EmailSender__Port", () => mailhog.GetEndpoint("smtp").Port.ToString())
    .WithEnvironment("EmailSender__Server", () => mailhog.GetEndpoint("smtp").Host)
    .WaitFor(membersAreaDb); // TODO: remove if startup don't need db

builder.Build().Run();
