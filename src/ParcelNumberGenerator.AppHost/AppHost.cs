// The composition root, and it exists for development (P1). `dotnet run` here brings up
// Postgres and the API together, wired, on a clone with nothing else installed.
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

IResourceBuilder<PostgresDatabaseResource> parcelNumbersDb = postgres.AddDatabase("parcelnumbersdb");

builder.AddProject<Projects.ParcelNumberGenerator_Api>("api")
    .WithReference(parcelNumbersDb)
    .WaitFor(parcelNumbersDb)
    .WithEnvironment("DATABASE_PROVIDER", "PostgreSQL")
    .WithHttpHealthCheck("/health");

await builder.Build().RunAsync();
