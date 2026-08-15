using ParcelNumberGenerator.Domain.Allocation;

namespace ParcelNumberGenerator.Domain;

/// <summary>
/// Coordinates allocation for the transport layer: one number, a batch of numbers, or a
/// description of what is left.
/// </summary>
/// <remarks>
/// Holds no algorithm of its own — the chosen <see cref="IAllocationStrategy"/> does that —
/// and no ASP.NET types, so the layering stays "endpoint binds and delegates, service
/// coordinates, strategy decides, store persists" (P9).
/// </remarks>
public sealed class ParcelNumberService(
    NumberPool pool,
    IAllocationStrategy strategy,
    IUsedNumberStore store)
{
    public NumberPool Pool { get; } = pool;

    public string StrategyName => strategy.Name;

    public Task<AllocationResult> AllocateAsync(CancellationToken cancellationToken) =>
        strategy.AllocateAsync(Pool, cancellationToken);

    /// <summary>
    /// Allocates up to <paramref name="count"/> numbers, stopping at the first failure.
    /// </summary>
    /// <remarks>
    /// Deliberately not atomic, and the result says so. Each number is claimed durably as it
    /// is drawn, so a batch that stops halfway has genuinely issued what it reports — the
    /// alternative, holding a transaction open across every claim in the batch, would make
    /// concurrent callers serialize behind each other for the whole request.
    /// </remarks>
    public async Task<BatchAllocationResult> AllocateManyAsync(int count, CancellationToken cancellationToken)
    {
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(count);

        List<int> allocated = new(count);
        for (int i = 0; i < count; i++)
        {
            AllocationResult result = await AllocateAsync(cancellationToken).ConfigureAwait(false);
            if (!result.IsSuccess)
            {
                return new BatchAllocationResult(allocated, result.Status);
            }

            allocated.Add(result.Number);
        }

        return new BatchAllocationResult(allocated, AllocationStatus.Allocated);
    }

    public async Task<PoolStatistics> GetStatisticsAsync(CancellationToken cancellationToken)
    {
        long used = await store.CountUsedInPoolAsync(Pool, cancellationToken).ConfigureAwait(false);
        return new PoolStatistics(Pool.Range, Pool.Exclusions, Pool.Capacity, used);
    }

    public Task<bool> IsUsedAsync(int number, CancellationToken cancellationToken) =>
        store.IsUsedAsync(number, cancellationToken);
}

/// <param name="Numbers">What was issued — possibly fewer than asked for.</param>
/// <param name="Status">
/// <see cref="AllocationStatus.Allocated"/> when the batch completed, otherwise why it
/// stopped early.
/// </param>
public sealed record BatchAllocationResult(IReadOnlyList<int> Numbers, AllocationStatus Status)
{
    public bool IsComplete => Status == AllocationStatus.Allocated;
}

public sealed record PoolStatistics(
    NumberRange Range,
    IReadOnlyList<NumberRange> Exclusions,
    long Capacity,
    long Used)
{
    public long Remaining => Capacity - Used;

    /// <summary>
    /// Issued fraction of the pool, 0 to 1. The number to watch: past ~0.9 the default
    /// strategy starts colliding and <c>sequential-scan</c> becomes the cheaper choice.
    /// </summary>
    public double Density => Capacity == 0 ? 1 : (double)Used / Capacity;
}
