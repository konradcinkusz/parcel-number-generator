using Microsoft.EntityFrameworkCore;
using ParcelNumberGenerator.Data;
using Testcontainers.PostgreSql;

namespace ParcelNumberGenerator.Tests.Infrastructure;

/// <summary>
/// A real PostgreSQL instance with this repository's committed migrations applied to it.
/// </summary>
/// <remarks>
/// <para>
/// DEV-3 recorded that persistence was tested only against EF Core's in-memory provider, so
/// two things had never been executed: the committed migrations had never been applied to a
/// relational engine, and the generator's concurrency guarantee — which rests on a primary
/// key violation raised by a database — was covered by construction rather than by running
/// it. This fixture is the apparatus for both.
/// </para>
/// <para>
/// The image is pinned to the one <c>docker-compose.yml</c> runs, so the engine under test
/// and the engine the documentation tells a reader to use are the same engine.
/// </para>
/// <para>
/// <b>Docker is optional.</b> <c>scripts/setup.sh</c> treats it as optional and the whole
/// no-Docker path in the README depends on that staying true, so a missing daemon skips
/// these tests rather than failing them. A contributor without Docker still gets a green
/// <c>dotnet test</c>; the skip announces itself, which a silent pass would not.
/// </para>
/// </remarks>
public sealed class PostgresFixture : IAsyncLifetime
{
    private const string Image = "postgres:17-alpine-DELIBERATELY-BROKEN-FOR-MUTATION-TEST";

    private PostgreSqlContainer? container;

    /// <summary>Whether the container started. False means Docker was unreachable.</summary>
    public bool Available { get; private set; }

    /// <summary>Why the fixture is unavailable, for the skip message.</summary>
    public string SkipReason { get; private set; } = "";

    public async ValueTask InitializeAsync()
    {
        try
        {
            // The image goes to the constructor: 4.14.0 obsoletes the parameterless one,
            // and TreatWarningsAsErrors turns that into a build failure rather than a
            // warning nobody reads.
            container = new PostgreSqlBuilder(Image)
                .WithDatabase("parcelnumbers")
                .Build();

            await container.StartAsync();

            // MigrateAsync, never EnsureCreated. EnsureCreated builds the schema from the
            // model, which would test precisely the thing DEV-3 says is untested: whether
            // the *committed migrations* produce a working schema on a real engine.
            // MigrateAsync applies only what is committed, which is also all a deployment
            // ever applies.
            await using ParcelNumbersDbContext db = CreateContext();
            await db.Database.MigrateAsync();

            Available = true;
        }
        catch (Exception ex)
        {
            // Docker absent, not running, or unreachable. Not a test failure — a test that
            // goes red for want of a daemon teaches everyone to ignore red.
            Available = false;
            SkipReason = $"Docker is not available, so {Image} could not be started: {ex.Message}";

            if (container is not null)
            {
                try
                {
                    await container.DisposeAsync();
                }
                catch (Exception disposeFailure)
                {
                    // Nothing useful to do: the container never started. Folding this into
                    // the skip reason keeps the original cause visible.
                    SkipReason += $" (cleanup also failed: {disposeFailure.Message})";
                }

                container = null;
            }
        }
    }

    /// <summary>
    /// A context bound to the running container.
    /// </summary>
    /// <remarks>
    /// A fresh context per call, deliberately. <see cref="DbContext"/> is not thread-safe,
    /// so a shared one would make a concurrency test measure the change tracker rather than
    /// the database — which is the exact substitution DEV-3 exists to correct. The provider
    /// is configured the way the service configures it, migrations assembly included, so
    /// what runs here is what runs in production.
    /// </remarks>
    public ParcelNumbersDbContext CreateContext()
    {
        if (container is null)
        {
            throw new InvalidOperationException(
                "The PostgreSQL container is not running. Guard with Available before calling this.");
        }

        DbContextOptions<ParcelNumbersDbContext> options =
            new DbContextOptionsBuilder<ParcelNumbersDbContext>()
                .UseNpgsql(
                    container.GetConnectionString(),
                    npgsql => npgsql.MigrationsAssembly("ParcelNumberGenerator.Migrations.PostgreSQL"))
                .Options;

        return new ParcelNumbersDbContext(options);
    }

    /// <summary>
    /// Set in an environment that is required to have Docker, so an unavailable fixture
    /// fails there instead of skipping.
    /// </summary>
    public const string RequireDockerVariable = "PNG_REQUIRE_DOCKER";

    /// <summary>Whether this environment has declared that Docker must be present.</summary>
    public static bool DockerIsRequired =>
        !string.IsNullOrEmpty(Environment.GetEnvironmentVariable(RequireDockerVariable));

    /// <summary>Skips the calling test when Docker is unavailable.</summary>
    /// <remarks>
    /// Unless <see cref="RequireDockerVariable"/> is set, in which case an unavailable
    /// fixture is a failure rather than a skip. Without that, a fixture broken so that it
    /// never starts would skip every test that depends on it, everywhere, and CI would stay
    /// green while testing nothing — and the summary cannot tell the two apart, because a
    /// dynamic skip is counted as a success.
    /// </remarks>
    public void SkipIfUnavailable()
    {
        if (Available)
        {
            return;
        }

        Assert.False(
            DockerIsRequired,
            $"{RequireDockerVariable} is set, so the PostgreSQL fixture was required to " +
            $"start and did not: {SkipReason}");

        Assert.Skip(SkipReason);
    }

    public async ValueTask DisposeAsync()
    {
        if (container is not null)
        {
            await container.DisposeAsync();
        }
    }
}

/// <summary>
/// Shares one container across every test in the collection.
/// </summary>
/// <remarks>
/// One container per test would put a Postgres start-up in front of each one, and a suite
/// slow enough to be switched off is not a gate. The tests are written so that sharing is
/// safe: each uses numbers no other test touches.
/// </remarks>
[CollectionDefinition(Name)]
public sealed class RealPostgres : ICollectionFixture<PostgresFixture>
{
    public const string Name = "postgres";
}
