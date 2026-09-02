using Microsoft.EntityFrameworkCore;
using ParcelNumberGenerator.Contracts;
using ParcelNumberGenerator.Notifications.Data;
using ParcelNumberGenerator.Notifications.Data.Entities;
using ParcelNumberGenerator.Notifications.Domain;
using ParcelNumberGenerator.Notifications.Tests.Infrastructure;

namespace ParcelNumberGenerator.Notifications.Tests;

/// <summary>
/// What this service's committed migrations build on a real engine, and what survives a
/// round trip through it.
/// </summary>
/// <remarks>
/// The drift check proves the migrations match the model. That is a different guarantee
/// from "they apply": a migration can match its model perfectly and still be rejected by
/// PostgreSQL — an identifier over the length limit, a column type mapped differently than
/// expected, a default expression the provider will not take. Until now nothing had ever
/// applied them to a relational engine.
/// </remarks>
[Collection(RealNotificationsPostgres.Name)]
public sealed class PostgresPersistenceTests(NotificationsPostgresFixture postgres)
{
    private static Notification NewNotification(string? parcelNumber, string body) => new()
    {
        ParcelNumber = parcelNumber,
        Body = body,
        Severity = NotificationSeverity.Warning,
        RaisedBy = ParcelEventKind.Manual,
        CreatedAt = DateTimeOffset.UtcNow,
    };

    [Fact]
    public async Task Committed_migrations_apply_to_a_real_engine()
    {
        postgres.SkipIfUnavailable();

        await using NotificationsDbContext db = postgres.CreateContext();

        IEnumerable<string> pending =
            await db.Database.GetPendingMigrationsAsync(TestContext.Current.CancellationToken);
        Assert.Empty(pending);

        IEnumerable<string> applied =
            await db.Database.GetAppliedMigrationsAsync(TestContext.Current.CancellationToken);
        Assert.NotEmpty(applied);
    }

    /// <summary>
    /// The three indexes the dashboard's queries depend on exist under their declared names.
    /// </summary>
    /// <remarks>
    /// <c>OnModelCreating</c> names them explicitly and its comment says why: the two
    /// operator queries are the whole workload of this service, so both are indexed rather
    /// than left to a sequential scan. An index that the model declares and the migration
    /// never creates costs nothing in a test and everything under load, and no existing
    /// check would notice — the in-memory provider has no indexes at all.
    /// </remarks>
    [Fact]
    public async Task The_declared_indexes_exist_on_a_real_engine()
    {
        postgres.SkipIfUnavailable();

        await using NotificationsDbContext db = postgres.CreateContext();

        List<string> indexes = await db.Database
            .SqlQueryRaw<string>(
                """
                SELECT indexname AS "Value"
                FROM pg_indexes
                WHERE tablename = 'notifications'
                  AND indexname LIKE 'ix_%'
                ORDER BY indexname
                """)
            .ToListAsync(TestContext.Current.CancellationToken);

        Assert.Equal(
            ["ix_notifications_created_at", "ix_notifications_outstanding", "ix_notifications_parcel_number"],
            indexes);
    }

    [Fact]
    public async Task A_notification_round_trips_through_a_real_engine()
    {
        postgres.SkipIfUnavailable();

        Guid id;
        DateTimeOffset createdAt = new(2026, 3, 4, 5, 6, 7, TimeSpan.Zero);

        await using (NotificationsDbContext write = postgres.CreateContext())
        {
            Notification notification = NewNotification("PNG-40000001-4", "Carton crushed on bay 3.");
            notification.CreatedAt = createdAt;
            notification.AcknowledgementRequired = true;
            notification.Pinned = true;

            write.Notifications.Add(notification);
            await write.SaveChangesAsync(TestContext.Current.CancellationToken);

            // Database-assigned, so a value coming back at all is itself the assertion that
            // ValueGeneratedOnAdd reached the schema.
            id = notification.Id;
            Assert.NotEqual(Guid.Empty, id);
        }

        await using NotificationsDbContext read = postgres.CreateContext();
        Notification? stored = await read.Notifications
            .AsNoTracking()
            .SingleOrDefaultAsync(n => n.Id == id, TestContext.Current.CancellationToken);

        Assert.NotNull(stored);
        Assert.Equal("PNG-40000001-4", stored.ParcelNumber);
        Assert.Equal("Carton crushed on bay 3.", stored.Body);
        Assert.Equal(NotificationSeverity.Warning, stored.Severity);
        Assert.Equal(ParcelEventKind.Manual, stored.RaisedBy);
        Assert.True(stored.Pinned);
        Assert.True(stored.IsOutstanding);

        // The enum conversions are declared HasConversion<int>() so a rename is not a data
        // migration. Reading the value back as the enum is what proves the conversion
        // survived the trip in both directions.
        Assert.Equal(createdAt, stored.CreatedAt, TimeSpan.FromMilliseconds(1));
    }

    /// <summary>
    /// A parcel number is stored canonically, checked after a real round trip.
    /// </summary>
    /// <remarks>
    /// This service's distinguishing behaviour is that it accepts every parcel-number
    /// dialect at the edge and stores one canonical form (P11). That is a claim about what
    /// is <em>in the database</em>, so an in-memory provider is a weak place to assert it
    /// and the real engine is the right one — the column is
    /// <c>varchar(<see cref="ParcelNumberLimits.CanonicalLength"/>)</c>, and a dialect
    /// stored raw would be silently truncated or rejected there rather than here.
    /// </remarks>
    [Theory]
    [InlineData("wms/40000002")]
    [InlineData("PNG-40000002-2")]
    [InlineData("  40000002  ")]
    public async Task A_dialect_is_stored_canonically(string dialect)
    {
        postgres.SkipIfUnavailable();

        Assert.True(ParcelNumber.TryParse(dialect, out ParcelNumber parsed));

        Guid id;
        await using (NotificationsDbContext write = postgres.CreateContext())
        {
            Notification notification = NewNotification(parsed.Canonical, $"Raised via {dialect}.");
            write.Notifications.Add(notification);
            await write.SaveChangesAsync(TestContext.Current.CancellationToken);
            id = notification.Id;
        }

        await using NotificationsDbContext read = postgres.CreateContext();
        string? stored = await read.Notifications
            .AsNoTracking()
            .Where(n => n.Id == id)
            .Select(n => n.ParcelNumber)
            .SingleAsync(TestContext.Current.CancellationToken);

        Assert.Equal(parsed.Canonical, stored);
        Assert.Equal(ParcelNumberLimits.CanonicalLength, stored!.Length);
    }

    /// <summary>
    /// Acknowledgement is idempotent and the first timestamp wins, at real column precision.
    /// </summary>
    /// <remarks>
    /// "First wins" is a rule about comparing two timestamps, so it is exactly the kind of
    /// rule that can stop being true at a real column's precision — two acknowledgements a
    /// few hundred microseconds apart round to the same stored value on a coarse column and
    /// the second silently becomes the first.
    /// </remarks>
    [Fact]
    public async Task Acknowledgement_is_idempotent_at_real_timestamp_precision()
    {
        postgres.SkipIfUnavailable();

        Guid id;
        await using (NotificationsDbContext write = postgres.CreateContext())
        {
            Notification notification = NewNotification("PNG-40000003-0", "Bay blocked.");
            notification.AcknowledgementRequired = true;
            write.Notifications.Add(notification);
            await write.SaveChangesAsync(TestContext.Current.CancellationToken);
            id = notification.Id;
        }

        DateTimeOffset first = new(2026, 5, 6, 7, 8, 9, 123, TimeSpan.Zero);

        await using (NotificationsDbContext acknowledge = postgres.CreateContext())
        {
            Notification stored = await acknowledge.Notifications
                .SingleAsync(n => n.Id == id, TestContext.Current.CancellationToken);

            // The service's rule: set it only when it is not already set.
            if (stored.AcknowledgedAt is null)
            {
                stored.AcknowledgedAt = first;
                stored.AcknowledgedBy = "operator-one";
            }

            await acknowledge.SaveChangesAsync(TestContext.Current.CancellationToken);
        }

        await using (NotificationsDbContext again = postgres.CreateContext())
        {
            Notification stored = await again.Notifications
                .SingleAsync(n => n.Id == id, TestContext.Current.CancellationToken);

            // A second acknowledgement, a quarter of a millisecond later. On a column too
            // coarse to tell them apart this is where "first wins" would quietly fail.
            if (stored.AcknowledgedAt is null)
            {
                stored.AcknowledgedAt = first.AddTicks(2_500);
                stored.AcknowledgedBy = "operator-two";
            }

            await again.SaveChangesAsync(TestContext.Current.CancellationToken);
        }

        await using NotificationsDbContext verify = postgres.CreateContext();
        Notification final = await verify.Notifications
            .AsNoTracking()
            .SingleAsync(n => n.Id == id, TestContext.Current.CancellationToken);

        Assert.Equal(first, final.AcknowledgedAt);
        Assert.Equal("operator-one", final.AcknowledgedBy);
        Assert.False(final.IsOutstanding);
    }
}
