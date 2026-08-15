using Microsoft.EntityFrameworkCore;
using ParcelNumberGenerator.Data;
using ParcelNumberGenerator.Domain;

namespace ParcelNumberGenerator.Tests;

/// <summary>
/// The persistence half of the concurrency guarantee. The strategy tests prove the
/// algorithm reacts correctly to losing a number; these prove the store is what makes
/// losing it possible in the first place.
/// </summary>
public sealed class EfUsedNumberStoreTests : IDisposable
{
    private readonly string _databaseName = Guid.NewGuid().ToString();

    private ParcelNumbersDbContext NewContext() =>
        new(new DbContextOptionsBuilder<ParcelNumbersDbContext>()
            .UseInMemoryDatabase(_databaseName)
            // The in-memory provider warns rather than fails on a transaction it cannot
            // honour; nothing here opens one, so leave the default and let it shout if that
            // ever changes.
            .Options);

    private static EfUsedNumberStore NewStore(ParcelNumbersDbContext context) =>
        new(context, TimeProvider.System);

    [Fact]
    public async Task Reserving_a_free_number_succeeds_and_records_it()
    {
        using ParcelNumbersDbContext context = NewContext();
        EfUsedNumberStore store = NewStore(context);

        Assert.True(await store.TryReserveAsync(42, CancellationToken.None));
        Assert.True(await store.IsUsedAsync(42, CancellationToken.None));
    }

    [Fact]
    public async Task Reserving_the_same_number_twice_from_one_context_reports_the_loss()
    {
        using ParcelNumbersDbContext context = NewContext();
        EfUsedNumberStore store = NewStore(context);

        Assert.True(await store.TryReserveAsync(42, CancellationToken.None));
        Assert.False(await store.TryReserveAsync(42, CancellationToken.None));
    }

    [Fact]
    public async Task A_rejected_reservation_does_not_poison_the_next_one()
    {
        using ParcelNumbersDbContext context = NewContext();
        EfUsedNumberStore store = NewStore(context);

        await store.TryReserveAsync(42, CancellationToken.None);
        await store.TryReserveAsync(42, CancellationToken.None);

        // Without detaching the rejected insert, this call re-submits it and fails on a
        // number the caller never asked about.
        Assert.True(await store.TryReserveAsync(43, CancellationToken.None));
        Assert.True(await store.IsUsedAsync(43, CancellationToken.None));
    }

    [Fact]
    public async Task Two_separate_contexts_cannot_both_claim_one_number()
    {
        // The shape of two concurrent requests: separate scopes, separate contexts, one
        // database. Exactly one claim must win.
        using ParcelNumbersDbContext first = NewContext();
        using ParcelNumbersDbContext second = NewContext();

        bool firstWon = await NewStore(first).TryReserveAsync(7, CancellationToken.None);
        bool secondWon = await NewStore(second).TryReserveAsync(7, CancellationToken.None);

        Assert.True(firstWon);
        Assert.False(secondWon);

        using ParcelNumbersDbContext verification = NewContext();
        Assert.Equal(1, await verification.UsedNumbers.CountAsync(CancellationToken.None));
    }

    [Fact]
    public async Task Counts_and_streams_only_what_is_inside_the_range()
    {
        using ParcelNumbersDbContext context = NewContext();
        EfUsedNumberStore store = NewStore(context);

        foreach (int number in (int[])[1, 5, 10, 15, 20])
        {
            await store.TryReserveAsync(number, CancellationToken.None);
        }

        Assert.Equal(3, await store.CountUsedAsync(new NumberRange(5, 15), CancellationToken.None));

        List<int> streamed = [];
        await foreach (int number in store.StreamUsedAsync(new NumberRange(5, 15), CancellationToken.None))
        {
            streamed.Add(number);
        }

        Assert.Equal([5, 10, 15], streamed);
    }

    [Fact]
    public async Task Streams_in_ascending_order_whatever_order_numbers_were_issued_in()
    {
        using ParcelNumbersDbContext context = NewContext();
        EfUsedNumberStore store = NewStore(context);

        // The gap walk in SequentialScanAllocationStrategy depends on this ordering and
        // would silently return a used number without it.
        foreach (int number in (int[])[9, 3, 7, 1])
        {
            await store.TryReserveAsync(number, CancellationToken.None);
        }

        List<int> streamed = [];
        await foreach (int number in store.StreamUsedAsync(new NumberRange(1, 10), CancellationToken.None))
        {
            streamed.Add(number);
        }

        Assert.Equal([1, 3, 7, 9], streamed);
    }

    [Fact]
    public async Task Records_when_a_number_was_issued()
    {
        DateTimeOffset now = new(2026, 8, 15, 12, 0, 0, TimeSpan.Zero);
        using ParcelNumbersDbContext context = NewContext();
        EfUsedNumberStore store = new(context, new FixedTimeProvider(now));

        await store.TryReserveAsync(1, CancellationToken.None);

        UsedNumber stored = await context.UsedNumbers.AsNoTracking().SingleAsync(CancellationToken.None);
        Assert.Equal(now, stored.AllocatedAtUtc);
    }

    public void Dispose()
    {
        using ParcelNumbersDbContext context = NewContext();
        context.Database.EnsureDeleted();
    }

    private sealed class FixedTimeProvider(DateTimeOffset now) : TimeProvider
    {
        public override DateTimeOffset GetUtcNow() => now;
    }
}
