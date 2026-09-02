using Microsoft.EntityFrameworkCore;
using ParcelNumberGenerator.Data;
using ParcelNumberGenerator.Domain;
using ParcelNumberGenerator.Domain.Allocation;
using ParcelNumberGenerator.Tests.Infrastructure;

namespace ParcelNumberGenerator.Tests;

/// <summary>
/// The promise the whole system rests on, against a real engine: a number is never issued
/// twice.
/// </summary>
/// <remarks>
/// <para>
/// DEV-3 recorded that this guarantee was covered <em>by construction</em> rather than by
/// execution — the mechanism is a primary key violation raised by a database and reported
/// as a lost race, and the in-memory provider raises it from its change tracker instead.
/// These tests execute it.
/// </para>
/// <para>
/// Every test here uses a band of numbers no other test touches, because the container is
/// shared across the collection.
/// </para>
/// </remarks>
[Collection(RealPostgres.Name)]
public sealed class PostgresConcurrencyTests(PostgresFixture postgres)
{
    /// <summary>How many callers race for one number. Enough to interleave, small enough to stay fast.</summary>
    private const int Racers = 32;

    /// <summary>
    /// One racer: its own context, and a store over it.
    /// </summary>
    /// <remarks>
    /// <para>
    /// <see cref="DbContext"/> is not thread-safe. Sharing one would make this measure the
    /// change tracker rather than the database, which is exactly the substitution DEV-3
    /// exists to correct — the test would pass and prove nothing.
    /// </para>
    /// <para>
    /// The context is carried alongside because <see cref="EfUsedNumberStore"/> does not own
    /// it: in the service it is resolved per request and disposed by the container, so here
    /// the test disposes it.
    /// </para>
    /// </remarks>
    private sealed record Racer(ParcelNumbersDbContext Context, EfUsedNumberStore Store);

    private Racer NewRacer()
    {
        ParcelNumbersDbContext context = postgres.CreateContext();
        return new Racer(context, new EfUsedNumberStore(context, TimeProvider.System));
    }

    /// <summary>The production wiring: a random probe, falling back to a sequential scan.</summary>
    private static AdaptiveAllocationStrategy StrategyOver(EfUsedNumberStore store) =>
        new(
            new RandomProbeAllocationStrategy(store, new SharedRandomSource()),
            new SequentialScanAllocationStrategy(store, new SharedRandomSource()));

    [Fact]
    public async Task Exactly_one_of_many_racers_claims_the_same_number()
    {
        postgres.SkipIfUnavailable();

        const int Contested = 4_200_001;
        CancellationToken cancellationToken = TestContext.Current.CancellationToken;

        // Released together, so the calls genuinely overlap. Starting tasks sequentially and
        // awaiting them with WhenAll is the shape that looks concurrent and is not: the
        // first can finish before the last begins, and then nothing has been tested.
        using SemaphoreSlim gate = new(0, Racers);
        List<Racer> racerSet = [.. Enumerable.Range(0, Racers).Select(_ => NewRacer())];

        try
        {
            Task<bool>[] racers = [.. racerSet.Select(async racer =>
            {
                await gate.WaitAsync(cancellationToken);
                return await racer.Store.TryReserveAsync(Contested, cancellationToken);
            })];

            gate.Release(Racers);
            bool[] outcomes = await Task.WhenAll(racers);

            // One winner, and every loser told it lost rather than throwing.
            Assert.Equal(1, outcomes.Count(won => won));
            Assert.Equal(Racers - 1, outcomes.Count(won => !won));

            // The assertion that actually matters. Return values alone would pass even if
            // the store had written the row twice; the row count is the guarantee.
            await using ParcelNumbersDbContext db = postgres.CreateContext();
            int rows = await db.UsedNumbers
                .AsNoTracking()
                .CountAsync(used => used.Number == Contested, cancellationToken);

            Assert.Equal(1, rows);
        }
        finally
        {
            foreach (Racer racer in racerSet)
            {
                await racer.Context.DisposeAsync();
            }
        }
    }

    /// <summary>
    /// A losing racer is reported as a loss, not as an error.
    /// </summary>
    /// <remarks>
    /// This is the specific behaviour the in-memory provider cannot exercise. PostgreSQL
    /// raises SQLSTATE 23505 through Npgsql as a <see cref="DbUpdateException"/>; the
    /// in-memory provider throws <see cref="InvalidOperationException"/> from its change
    /// tracker, or a bare <see cref="ArgumentException"/> from its table writer. The store
    /// catches all three and then confirms by re-reading rather than by matching an error
    /// code — which is what makes it portable, and what this test proves actually holds
    /// against the engine that will run in production.
    /// </remarks>
    [Fact]
    public async Task A_lost_race_is_reported_rather_than_thrown()
    {
        postgres.SkipIfUnavailable();

        const int Contested = 4_200_002;
        CancellationToken cancellationToken = TestContext.Current.CancellationToken;

        Racer winner = NewRacer();
        Racer loser = NewRacer();

        try
        {
            Assert.True(await winner.Store.TryReserveAsync(Contested, cancellationToken));

            // No exception escapes, and the answer is false rather than an error.
            Assert.False(await loser.Store.TryReserveAsync(Contested, cancellationToken));

            // The loser's context is still usable afterwards. A rejected insert left
            // attached would be resubmitted by the next SaveChanges and fail an unrelated
            // allocation — the failure the store's finally block exists to prevent, here
            // against a real engine rather than against the in-memory provider.
            Assert.True(await loser.Store.TryReserveAsync(Contested + 1, cancellationToken));
        }
        finally
        {
            await winner.Context.DisposeAsync();
            await loser.Context.DisposeAsync();
        }
    }

    /// <summary>
    /// Concurrent allocation of distinct numbers issues each one once.
    /// </summary>
    /// <remarks>
    /// The end-to-end property, through the real allocation strategy rather than through
    /// direct store calls. The failure with consequences is a caller told it owns a number
    /// that was never persisted: a parcel is labelled with it, and the system later hands
    /// the same number to somebody else.
    /// </remarks>
    [Fact]
    public async Task Concurrent_allocation_issues_every_number_exactly_once()
    {
        postgres.SkipIfUnavailable();

        CancellationToken cancellationToken = TestContext.Current.CancellationToken;

        // A pool this size against 32 racers guarantees genuine contention: the strategy
        // will draw an already-taken number and have to retry.
        NumberPool pool = NumberPool.Create(new NumberRange(4_300_001, 4_300_048));
        List<Racer> racerSet = [.. Enumerable.Range(0, Racers).Select(_ => NewRacer())];

        try
        {
            using SemaphoreSlim gate = new(0, Racers);

            Task<AllocationResult>[] racers = [.. racerSet.Select(async racer =>
            {
                AdaptiveAllocationStrategy strategy = StrategyOver(racer.Store);
                await gate.WaitAsync(cancellationToken);
                return await strategy.AllocateAsync(pool, cancellationToken);
            })];

            gate.Release(Racers);
            AllocationResult[] results = await Task.WhenAll(racers);

            int[] issued = [.. results.Where(r => r.IsSuccess).Select(r => r.Number)];

            // Every number the callers were given is distinct and inside the pool.
            Assert.Equal(issued.Length, issued.Distinct().Count());
            Assert.All(issued, number => Assert.InRange(number, pool.Range.From, pool.Range.To));

            // And what the callers hold is exactly what the database holds — no caller was
            // told it owns a number that was never written, and nothing was written that no
            // caller was told about.
            await using ParcelNumbersDbContext db = postgres.CreateContext();
            List<int> persisted = await db.UsedNumbers
                .AsNoTracking()
                .Where(used => used.Number >= pool.Range.From && used.Number <= pool.Range.To)
                .Select(used => used.Number)
                .ToListAsync(cancellationToken);

            Assert.Equal([.. issued.Order()], [.. persisted.Order()]);
        }
        finally
        {
            foreach (Racer racer in racerSet)
            {
                await racer.Context.DisposeAsync();
            }
        }
    }
}
