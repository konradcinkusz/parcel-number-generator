// The composition root, and it exists for development (P1). `dotnet run` here brings up
// Postgres, both services and the operator console together, wired, on a clone with
// nothing else installed.
//
// This is not the production topology. Production is described by flyio/*.fly.toml and the
// deploy workflow; treating this file as a second source of truth for it is what produces
// drift between the two.

IDistributedApplicationBuilder builder = DistributedApplication.CreateBuilder(args);

IResourceBuilder<PostgresServerResource> postgres = builder
    .AddPostgres("postgres")
    // Survives a restart of the AppHost, so numbers issued while developing stay issued and
    // the exhaustion path is reachable without re-seeding by hand.
    .WithDataVolume("parcelnumbers-pgdata")
    .WithPgAdmin();

// P3 — one database per service, each owned by exactly one of them. Physical co-location
// on one dev Postgres instance is a cost decision; a service opening the other's database
// would not be.
IResourceBuilder<PostgresDatabaseResource> parcelNumbersDb = postgres.AddDatabase("parcelnumbersdb");
IResourceBuilder<PostgresDatabaseResource> notificationsDb = postgres.AddDatabase("notificationsdb");

IResourceBuilder<ProjectResource> api = builder.AddProject<Projects.ParcelNumberGenerator_Api>("api")
    .WithReference(parcelNumbersDb)
    .WaitFor(parcelNumbersDb)
    .WithEnvironment("DATABASE_PROVIDER", "PostgreSQL")
    .WithHttpHealthCheck("/health");

IResourceBuilder<ProjectResource> notifications = builder
    .AddProject<Projects.ParcelNumberGenerator_Notifications>("notifications")
    .WithReference(notificationsDb)
    .WaitFor(notificationsDb)
    .WithEnvironment("DATABASE_PROVIDER", "PostgreSQL")
    .WithHttpHealthCheck("/health");

// The console. WithReference feeds the BFF's candidate ladder through service discovery,
// so the proxy finds both services with zero configuration here.
builder.AddProject<Projects.ParcelNumberGenerator_Web>("web")
    .WithReference(api)
    .WithReference(notifications)
    .WaitFor(api)
    .WaitFor(notifications)
    .WithHttpHealthCheck("/health")
    .WithExternalHttpEndpoints();

await builder.Build().RunAsync();
