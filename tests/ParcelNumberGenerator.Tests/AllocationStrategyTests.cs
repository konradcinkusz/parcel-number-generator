using ParcelNumberGenerator.Domain;
using ParcelNumberGenerator.Domain.Allocation;
using ParcelNumberGenerator.Tests.Infrastructure;

namespace ParcelNumberGenerator.Tests;

public sealed class RandomProbeAllocationStrategyTests
{
    private static readonly NumberPool SmallPool = NumberPool.Create(new NumberRange(1, 10));

    [Fact]
    public async Task Allocates_the_number_the_draw_maps_to()
    {
        FakeUsedNumberStore store = new();
        RandomProbeAllocationStrategy strategy = new(store, new ScriptedRandomSource(3));

        AllocationResult result = await strategy.AllocateAsync(SmallPool, CancellationToken.None);

        Assert.Equal(AllocationStatus.Allocated, result.Status);
        Assert.Equal(4, result.Number);
        Assert.Equal(1, result.Attempts);
    }

    [Fact]
    public async Task Never_returns_a_number_inside_an_exclusion()
    {
        NumberPool pool = NumberPool.Create(new NumberRange(1, 10), [new NumberRange(4, 6)]);
        FakeUsedNumberStore store = new();
        RandomProbeAllocationStrategy strategy = new(store, new ScriptedRandomSource(0, 1, 2, 3, 4, 5, 6));

        for (int i = 0; i < pool.Capacity; i++)
        {
            AllocationResult result = await strategy.AllocateAsync(pool, CancellationToken.None);
            Assert.True(result.IsSuccess);
            Assert.False(result.Number is >= 4 and <= 6, $"{result.Number} is excluded.");
        }
    }

    [Fact]
    public async Task Retries_past_a_number_that_is_already_used()
    {
        FakeUsedNumberStore store = new();
        store.Seed(1, 2);

        // Draws indices 0 and 1 (numbers 1 and 2, both taken) before index 2.
        RandomProbeAllocationStrategy strategy = new(store, new ScriptedRandomSource(0, 1, 2));

        AllocationResult result = await strategy.AllocateAsync(SmallPool, CancellationToken.None);

        Assert.Equal(3, result.Number);
        Assert.Equal(3, result.Attempts);
    }

    [Fact]
    public async Task Reports_exhaustion_when_every_number_is_taken()
    {
        FakeUsedNumberStore store = new();
        store.Seed(Enumerable.Range(1, 10));
        RandomProbeAllocationStrategy strategy = new(store, new ScriptedRandomSource(0, 0), maxAttempts: 2);

        AllocationResult result = await strategy.AllocateAsync(SmallPool, CancellationToken.None);

        Assert.Equal(AllocationStatus.PoolExhausted, result.Status);
    }

    [Fact]
    public async Task Reports_contention_rather_than_exhaustion_when_the_pool_still_has_room()
    {
        FakeUsedNumberStore store = new();
        store.Seed(1, 2);
        RandomProbeAllocationStrategy strategy = new(store, new ScriptedRandomSource(0, 1), maxAttempts: 2);

        AllocationResult result = await strategy.AllocateAsync(SmallPool, CancellationToken.None);

        // The distinction the caller acts on: 503-and-retry, not 409-and-stop.
        Assert.Equal(AllocationStatus.Contended, result.Status);
    }

    [Fact]
    public async Task Loses_a_number_claimed_between_the_draw_and_the_reservation()
    {
        FakeUsedNumberStore store = new();

        // A concurrent caller takes the first candidate in the instant before this one
        // claims it. The strategy must lose that race and move on, not overwrite it.
        bool stolen = false;
        store.BeforeReserve = number =>
        {
            if (!stolen)
            {
                stolen = true;
                store.Seed(number);
            }
        };

        RandomProbeAllocationStrategy strategy = new(store, new ScriptedRandomSource(0, 1));
        AllocationResult result = await strategy.AllocateAsync(SmallPool, CancellationToken.None);

        Assert.Equal(2, result.Number);
        Assert.Equal(2, result.Attempts);
    }

    [Fact]
    public async Task An_empty_pool_is_exhausted_without_touching_the_store()
    {
        NumberPool empty = NumberPool.Create(new NumberRange(1, 10), [new NumberRange(1, 10)]);
        FakeUsedNumberStore store = new();
        RandomProbeAllocationStrategy strategy = new(store, new ScriptedRandomSource());

        AllocationResult result = await strategy.AllocateAsync(empty, CancellationToken.None);

        Assert.Equal(AllocationStatus.PoolExhausted, result.Status);
        Assert.Equal(0, store.ReserveAttempts);
    }

    [Fact]
    public void Rejects_a_non_positive_attempt_budget()
    {
        Assert.Throws<ArgumentOutOfRangeException>(() =>
            new RandomProbeAllocationStrategy(new FakeUsedNumberStore(), new ScriptedRandomSource(), maxAttempts: 0));
    }
}

public sealed class SequentialScanAllocationStrategyTests
{
    private static readonly NumberPool SmallPool = NumberPool.Create(new NumberRange(1, 10));

    [Fact]
    public async Task Picks_the_k_th_free_number()
    {
        FakeUsedNumberStore store = new();
        store.Seed(1, 2, 3);

        // Seven free numbers remain (4..10); rank 2 is the third of them.
        SequentialScanAllocationStrategy strategy = new(store, new ScriptedRandomSource(2));

        AllocationResult result = await strategy.AllocateAsync(SmallPool, CancellationToken.None);

        Assert.Equal(6, result.Number);
    }

    [Fact]
    public async Task Picks_a_free_number_from_between_two_used_ones()
    {
        FakeUsedNumberStore store = new();
        store.Seed(1, 2, 4, 5, 6, 7, 8, 9, 10);

        // Exactly one number is free, so rank 0 must find it.
        SequentialScanAllocationStrategy strategy = new(store, new ScriptedRandomSource(0));

        AllocationResult result = await strategy.AllocateAsync(SmallPool, CancellationToken.None);

        Assert.Equal(3, result.Number);
    }

    [Fact]
    public async Task Drains_the_pool_exactly_once_each()
    {
        FakeUsedNumberStore store = new();
        NumberPool pool = NumberPool.Create(new NumberRange(1, 10), [new NumberRange(5, 6)]);

        // Always take the first free number: eight allocations, then exhaustion.
        SequentialScanAllocationStrategy strategy = new(store, new ScriptedRandomSource([.. new long[9]]));

        List<int> allocated = [];
        for (int i = 0; i < 8; i++)
        {
            AllocationResult result = await strategy.AllocateAsync(pool, CancellationToken.None);
            Assert.True(result.IsSuccess);
            allocated.Add(result.Number);
        }

        Assert.Equal([1, 2, 3, 4, 7, 8, 9, 10], allocated);

        AllocationResult exhausted = await strategy.AllocateAsync(pool, CancellationToken.None);
        Assert.Equal(AllocationStatus.PoolExhausted, exhausted.Status);
    }

    [Fact]
    public async Task Ignores_used_numbers_that_sit_inside_an_exclusion()
    {
        FakeUsedNumberStore store = new();

        // Issued before the exclusion existed, so they are in the table but not in the pool.
        // Counting them against the pool would report it as fuller than it is — and at the
        // boundary, as exhausted while free numbers remain.
        store.Seed(5, 6);

        NumberPool pool = NumberPool.Create(new NumberRange(1, 10), [new NumberRange(5, 6)]);
        SequentialScanAllocationStrategy strategy = new(store, new ScriptedRandomSource(7));

        AllocationResult result = await strategy.AllocateAsync(pool, CancellationToken.None);

        Assert.Equal(10, result.Number);
    }

    [Fact]
    public async Task Reports_exhaustion_when_nothing_is_free()
    {
        FakeUsedNumberStore store = new();
        store.Seed(Enumerable.Range(1, 10));
        SequentialScanAllocationStrategy strategy = new(store, new ScriptedRandomSource());

        AllocationResult result = await strategy.AllocateAsync(SmallPool, CancellationToken.None);

        Assert.Equal(AllocationStatus.PoolExhausted, result.Status);
    }

    [Fact]
    public async Task Reports_contention_when_it_keeps_losing_the_number_it_found()
    {
        FakeUsedNumberStore store = new();
        store.BeforeReserve = number => store.Seed(number);

        SequentialScanAllocationStrategy strategy = new(store, new ScriptedRandomSource(0, 0), maxAttempts: 2);
        AllocationResult result = await strategy.AllocateAsync(SmallPool, CancellationToken.None);

        Assert.Equal(AllocationStatus.Contended, result.Status);
    }
}
