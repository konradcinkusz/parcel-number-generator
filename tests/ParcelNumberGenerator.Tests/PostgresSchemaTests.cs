using Microsoft.EntityFrameworkCore;
using ParcelNumberGenerator.Data;
using ParcelNumberGenerator.Tests.Infrastructure;

namespace ParcelNumberGenerator.Tests;

/// <summary>
/// What the committed migrations actually build on a real engine.
/// </summary>
/// <remarks>
/// <para>
/// <c>SchemaTests</c> asserts that the primary key is declared in both committed migration
/// sets, by reading the migration source. That is a statement about what the repository
/// says. These tests are a statement about what PostgreSQL does with it — the two can come
/// apart, and DEV-3 exists because nothing here had ever checked the second.
/// </para>
/// <para>
/// The assertions read the live catalogue rather than the EF model, on purpose: asking EF
/// what it thinks the schema is would answer from the same model the migrations were
/// generated from, and agree with itself no matter what the database contains.
/// </para>
/// </remarks>
[Collection(PostgresCollection.Name)]
public sealed class PostgresSchemaTests(PostgresFixture postgres)
{
    [Fact]
    public async Task Committed_migrations_apply_to_a_real_engine()
    {
        postgres.SkipIfUnavailable();

        await using ParcelNumbersDbContext db = postgres.CreateContext();

        // Applied, not pending. An empty set here means MigrateAsync ran everything the
        // repository has committed.
        IEnumerable<string> pending = await db.Database.GetPendingMigrationsAsync();
        Assert.Empty(pending);

        IEnumerable<string> applied = await db.Database.GetAppliedMigrationsAsync();
        Assert.NotEmpty(applied);
    }

    [Fact]
    public async Task The_migrated_schema_has_the_used_numbers_table()
    {
        postgres.SkipIfUnavailable();

        await using ParcelNumbersDbContext db = postgres.CreateContext();

        List<string> columns = await db.Database
            .SqlQueryRaw<string>(
                """
                SELECT column_name AS "Value"
                FROM information_schema.columns
                WHERE table_name = 'used_numbers'
                ORDER BY column_name
                """)
            .ToListAsync();

        // The table exists and carries exactly the two columns the model declares, under
        // the snake_case names OnModelCreating maps them to.
        Assert.Equal(["allocated_at_utc", "number"], columns);
    }

    /// <summary>
    /// The assertion the whole fixture exists for.
    /// </summary>
    /// <remarks>
    /// The generator's promise is that a number is never issued twice, and the mechanism is
    /// a primary key violation raised by the engine and reported as a lost race. If the
    /// migration did not create this constraint, every concurrency guarantee in the system
    /// would rest on nothing — and every existing test would still pass, because the
    /// in-memory provider enforces uniqueness from its change tracker whatever the schema
    /// says.
    /// </remarks>
    [Fact]
    public async Task Number_is_the_primary_key_on_a_real_engine()
    {
        postgres.SkipIfUnavailable();

        await using ParcelNumbersDbContext db = postgres.CreateContext();

        List<string> keyColumns = await db.Database
            .SqlQueryRaw<string>(
                """
                SELECT key_column.column_name AS "Value"
                FROM information_schema.table_constraints AS constraints
                JOIN information_schema.key_column_usage AS key_column
                  ON key_column.constraint_name = constraints.constraint_name
                WHERE constraints.table_name = 'used_numbers'
                  AND constraints.constraint_type = 'PRIMARY KEY'
                ORDER BY key_column.ordinal_position
                """)
            .ToListAsync();

        Assert.Equal(["number"], keyColumns);
    }

    /// <summary>
    /// The constraint refuses a second insert of the same number.
    /// </summary>
    /// <remarks>
    /// The catalogue says the key is declared; this says the engine enforces it. Sequential
    /// rather than concurrent on purpose — that this is the *arbiter under contention* is
    /// #25's subject, and it needs this to be true first.
    /// </remarks>
    [Fact]
    public async Task A_duplicate_insert_is_refused_by_the_engine()
    {
        postgres.SkipIfUnavailable();

        const int Number = 4_100_001;

        await using (ParcelNumbersDbContext first = postgres.CreateContext())
        {
            first.UsedNumbers.Add(new UsedNumber { Number = Number, AllocatedAtUtc = DateTimeOffset.UtcNow });
            await first.SaveChangesAsync();
        }

        await using ParcelNumbersDbContext second = postgres.CreateContext();
        second.UsedNumbers.Add(new UsedNumber { Number = Number, AllocatedAtUtc = DateTimeOffset.UtcNow });

        await Assert.ThrowsAsync<DbUpdateException>(() => second.SaveChangesAsync());
    }
}
