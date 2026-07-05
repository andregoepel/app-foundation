var builder = DistributedApplication.CreateBuilder(args);

// A PostgreSQL container with a persistent volume so setup and accounts survive restarts.
var postgres = builder.AddPostgres("postgres").WithDataVolume();

// The database resource name is the connection-string name the foundation reads by default
// (AppFoundationOptions.DatabaseConnectionName == "appfoundation-database").
var database = postgres.AddDatabase("appfoundation-database", "appfoundation");

// The sample web app, wired to the database and started only once it is ready.
builder
    .AddProject<Projects.AndreGoepel_AppFoundation_Sample>("web")
    .WithReference(database)
    .WaitFor(database);

builder.Build().Run();
