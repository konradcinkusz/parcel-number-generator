namespace ParcelNumberGenerator.Domain.Allocation;

/// <summary>
/// Draws a uniformly random allocation index, claims the number it maps to, and retries on
/// a collision. The default, and the right choice while the pool is mostly free.
/// </summary>
/// <remarks>
/// <para>
/// One database round trip per attempt, and the expected number of attempts is
/// <c>1 / (1 - density)</c> — under two while the pool is less than half issued. It
/// degrades as the pool fills, which is what <see cref="SequentialScanAllocationStrategy"/>
/// exists for.
/// </para>
/// <para>
/// The legacy equivalent probed a random number and then ran a binary search over the used
/// table to decide whether it was free — and that search issued <em>one SQL query per
/// comparison</em>, so a single allocation cost O(log n) round trips before it even tried to
/// insert. Claiming the number is the membership test; there is no search.
/// </para>
/// </remarks>
public sealed class RandomProbeAllocationStrategy(
    IUsedNumberStore store,
    IRandomSource random,
    int maxAttempts = RandomProbeAllocationStrategy.DefaultMaxAttempts) : IAllocationStrategy
{
    public const string StrategyName = "random-probe";

    /// <summary>
    /// Enough that losing every attempt means real contention rather than bad luck: at 50%
    /// density the chance of 16 consecutive collisions is about 1 in 65,000.
    /// </summary>
    public const int DefaultMaxAttempts = 16;

    private readonly int _maxAttempts = maxAttempts > 0
        ? maxAttempts
        : throw new ArgumentOutOfRangeException(nameof(maxAttempts));

    public string Name => StrategyName;

    public async Task<AllocationResult> AllocateAsync(NumberPool pool, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(pool);

        if (pool.Capacity == 0)
        {
            return AllocationResult.PoolExhausted();
        }

        for (int attempt = 1; attempt <= _maxAttempts; attempt++)
        {
            cancellationToken.ThrowIfCancellationRequested();

            int candidate = pool.NumberAt(random.NextInt64(pool.Capacity));
            if (await store.TryReserveAsync(candidate, cancellationToken).ConfigureAwait(false))
            {
                return AllocationResult.Allocated(candidate, attempt);
            }
        }

        // Out of attempts. Distinguish "the pool is full" from "we were unlucky", because
        // the caller's response differs: one is a 409 that will never succeed, the other a
        // 503 worth retrying. This costs a COUNT, and only on the unhappy path.
        long used = await store.CountUsedInPoolAsync(pool, cancellationToken).ConfigureAwait(false);
        return used >= pool.Capacity
            ? AllocationResult.PoolExhausted()
            : AllocationResult.Contended(_maxAttempts);
    }
}
