var builder = DistributedApplication.CreateBuilder(args);

var identityDbUser = builder.AddParameter("database-user", "db-user");
;
var identityDbPassword = builder.AddParameter("database-password", "Password1+", true);
;

var postgresServer = builder
    .AddPostgres("postgres-server", identityDbUser, identityDbPassword)
    .WithLifetime(ContainerLifetime.Persistent)
    .WithHostPort(5432)
    .WithDataVolume();
var membersAreaDb = postgresServer.AddDatabase("members-area-database");

builder
    .AddProject<Projects.AndreGoepel_MembersArea>("andregoepel-membersarea")
    .WithReference(membersAreaDb)
    .WaitFor(membersAreaDb); // TODO: remove if startup don't need db

builder.Build().Run();
