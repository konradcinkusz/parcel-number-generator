using ParcelNumberGenerator.Domain;
using ParcelNumberGenerator.Domain.Allocation;
using ParcelNumberGenerator.Tests.Infrastructure;

namespace ParcelNumberGenerator.Tests;

public sealed class AdaptiveAllocationStrategyTests
{
    private static readonly NumberPool SmallPool = NumberPool.Create(new NumberRange(1, 10));

    private static AdaptiveAllocationStrategy Build(
        FakeUsedNumberStore store,
        IRandomSource probeDraws,
        IRandomSource scanDraws,
        int probeAttempts = 2) =>
        new(
            new RandomProbeAllocationStrategy(store, probeDraws, probeAttempts),
            new SequentialScanAllocationStrategy(store, scanDraws));

    [Fact]
    public async Task Returns_the_probe_result_when_probing_succeeds()
    {
        FakeUsedNumberStore store = new();
        AdaptiveAllocationStrategy strategy = Build(store, new ScriptedRandomSource(3), new ScriptedRandomSource());

        AllocationResult result = await strategy.AllocateAsync(SmallPool, CancellationToken.None);

        // The scan source is scripted with nothing, so reaching it at all would throw.
        Assert.Equal(4, result.Number);
    }

    [Fact]
    public async Task Escalates_to_a_scan_when_probing_runs_out_of_attempts()
    {
        FakeUsedNumberStore store = new();
        store.Seed(Enumerable.Range(1, 9));

        // One number is free (10) and probing keeps drawing taken ones, which is the
        // situation the whole strategy exists for: the pool is not empty, but uniform
        // probing cannot finish it.
        AdaptiveAllocationStrategy strategy = Build(
            store,
            probeDraws: new ScriptedRandomSource(0, 1),
            scanDraws: new ScriptedRandomSource(0));

        AllocationResult result = await strategy.AllocateAsync(SmallPool, CancellationToken.None);

        Assert.Equal(AllocationStatus.Allocated, result.Status);
        Assert.Equal(10, result.Number);
    }

    [Fact]
    public async Task Does_not_scan_a_pool_that_is_genuinely_exhausted()
    {
        FakeUsedNumberStore store = new();
        store.Seed(Enumerable.Range(1, 10));

        // Probing reports PoolExhausted, not Contended. An empty scan source proves the scan
        // is skipped — confirming exhaustion the slow way would be pure waste.
        AdaptiveAllocationStrategy strategy = Build(
            store,
            probeDraws: new ScriptedRandomSource(0, 1),
            scanDraws: new ScriptedRandomSource());

        AllocationResult result = await strategy.AllocateAsync(SmallPool, CancellationToken.None);

        Assert.Equal(AllocationStatus.PoolExhausted, result.Status);
    }

    [Fact]
    public void Reports_its_own_name_rather_than_the_strategy_it_delegated_to()
    {
        FakeUsedNumberStore store = new();
        AdaptiveAllocationStrategy strategy = Build(store, new ScriptedRandomSource(), new ScriptedRandomSource());

        Assert.Equal("adaptive", strategy.Name);
    }
}
