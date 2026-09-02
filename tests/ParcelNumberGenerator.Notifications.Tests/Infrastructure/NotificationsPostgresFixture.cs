using Microsoft.EntityFrameworkCore;
using ParcelNumberGenerator.Notifications.Data;
using Testcontainers.PostgreSql;

namespace ParcelNumberGenerator.Notifications.Tests.Infrastructure;

/// <summary>
/// A real PostgreSQL instance with the notification service's committed migrations applied.
/// </summary>
/// <remarks>
/// <para>
/// DEV-3 covers both services and says so: "and, now that the notification service lives
/// here too, the same fixture exercising its migrations". This is that, for this service.
/// </para>
/// <para>
/// Deliberately a second fixture rather than one shared with the generator's suite. The two
/// differ in every part that matters — a different <see cref="DbContext"/>, a different
/// migrations assembly, a different database — and what they share is about thirty lines of
/// start-a-container-and-skip-if-you-cannot. A shared test-support project would add a
/// project to the solution and couple two suites together to save that. Two similar
/// fixtures are cheaper than the wrong abstraction; if a third service ever arrives, the
/// duplication will have earned the extraction.
/// </para>
/// <para>
/// <b>Docker is optional</b>, exactly as in the generator's fixture: a missing daemon skips
/// these tests rather than failing them, because <c>scripts/setup.sh</c> treats Docker as
/// optional and the README's no-Docker path depends on that staying true.
/// </para>
/// </remarks>
public sealed class NotificationsPostgresFixture : IAsyncLifetime
{
    private const string Image = "postgres:17-alpine";

    private PostgreSqlContainer? container;

    public bool Available { get; private set; }

    public string SkipReason { get; private set; } = "";

    public async ValueTask InitializeAsync()
    {
        try
        {
            container = new PostgreSqlBuilder(Image)
                .WithDatabase("notifications")
                .Build();

            await container.StartAsync();

            // MigrateAsync, never EnsureCreated: the point is whether the *committed*
            // migrations build a working schema, and EnsureCreated would build it from the
            // model instead and agree with itself.
            await using NotificationsDbContext db = CreateContext();
            await db.Database.MigrateAsync();

            Available = true;
        }
        catch (Exception ex)
        {
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
                    SkipReason += $" (cleanup also failed: {disposeFailure.Message})";
                }

                container = null;
            }
        }
    }

    public NotificationsDbContext CreateContext()
    {
        if (container is null)
        {
            throw new InvalidOperationException(
                "The PostgreSQL container is not running. Guard with Available before calling this.");
        }

        DbContextOptions<NotificationsDbContext> options =
            new DbContextOptionsBuilder<NotificationsDbContext>()
                .UseNpgsql(
                    container.GetConnectionString(),
                    npgsql => npgsql.MigrationsAssembly("ParcelNumberGenerator.Notifications.Migrations.PostgreSQL"))
                .Options;

        return new NotificationsDbContext(options);
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
    /// Unless <see cref="RequireDockerVariable"/> is set. See the generator suite's fixture
    /// for the reasoning: a fixture broken so that it never starts would skip everywhere
    /// and CI would stay green while testing nothing.
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

/// <summary>Shares one container across the collection.</summary>
[CollectionDefinition(Name)]
public sealed class RealNotificationsPostgres : ICollectionFixture<NotificationsPostgresFixture>
{
    public const string Name = "notifications-postgres";
}
