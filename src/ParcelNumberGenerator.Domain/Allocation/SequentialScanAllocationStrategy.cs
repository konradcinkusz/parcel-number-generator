namespace ParcelNumberGenerator.Domain.Allocation;

/// <summary>
/// Picks the k-th still-free number for a uniformly random k, by streaming the issued
/// numbers in order and counting the gaps between them. For pools that are nearly full.
/// </summary>
/// <remarks>
/// <para>
/// Cost is one pass over the issued numbers rather than one round trip per attempt, so it
/// beats <see cref="RandomProbeAllocationStrategy"/> exactly where that strategy is worst:
/// at 99% density random probing expects a hundred collisions per allocation, while this
/// one still succeeds on its first claim. Below roughly 90% density it is the slower of the
/// two, which is why the default is the other one.
/// </para>
/// <para>
/// The distribution is the same as random probing's — uniform over the free numbers, not
/// over the range — because k indexes the free numbers, not the pool.
/// </para>
/// </remarks>
public sealed class SequentialScanAllocationStrategy(
    IUsedNumberStore store,
    IRandomSource random,
    int maxAttempts = SequentialScanAllocationStrategy.DefaultMaxAttempts) : IAllocationStrategy
{
    public const string StrategyName = "sequential-scan";

    /// <summary>
    /// Lower than the random strategy's budget on purpose: each attempt is a full scan, so
    /// retrying is expensive, and losing a claim twice means contention a third pass will
    /// not resolve either.
    /// </summary>
    public const int DefaultMaxAttempts = 4;

    private readonly int _maxAttempts = maxAttempts > 0
        ? maxAttempts
        : throw new ArgumentOutOfRangeException(nameof(maxAttempts));

    public string Name => StrategyName;

    public async Task<AllocationResult> AllocateAsync(NumberPool pool, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(pool);

        for (int attempt = 1; attempt <= _maxAttempts; attempt++)
        {
            cancellationToken.ThrowIfCancellationRequested();

            long used = await store.CountUsedInPoolAsync(pool, cancellationToken).ConfigureAwait(false);
            long free = pool.Capacity - used;
            if (free <= 0)
            {
                return AllocationResult.PoolExhausted();
            }

            long? index = await FindFreeIndexAsync(pool, random.NextInt64(free), cancellationToken)
                .ConfigureAwait(false);

            // Null means the count and the scan disagreed, which only happens when another
            // caller committed between them. That is contention, so retry rather than fail.
            if (index is not null &&
                await store.TryReserveAsync(pool.NumberAt(index.Value), cancellationToken).ConfigureAwait(false))
            {
                return AllocationResult.Allocated(pool.NumberAt(index.Value), attempt);
            }
        }

        return AllocationResult.Contended(_maxAttempts);
    }

    /// <summary>
    /// The allocation index of the <paramref name="rank"/>-th free number, counting from
    /// zero, or <c>null</c> if the pool filled up mid-scan.
    /// </summary>
    /// <remarks>
    /// Standard k-th-missing walk: every issued number at or before the running target
    /// pushes the target one slot further along. Both sequences are ascending, so one pass
    /// is enough and nothing needs to be held in memory.
    /// </remarks>
    private async Task<long?> FindFreeIndexAsync(NumberPool pool, long rank, CancellationToken cancellationToken)
    {
        long target = rank;

        await foreach (int usedNumber in store.StreamUsedInPoolAsync(pool, cancellationToken).ConfigureAwait(false))
        {
            // Non-null by construction: StreamUsedInPoolAsync only yields numbers from the
            // pool's own segments.
            long usedIndex = pool.IndexOf(usedNumber)!.Value;
            if (usedIndex > target)
            {
                break;
            }

            target++;
        }

        return target < pool.Capacity ? target : null;
    }
}
