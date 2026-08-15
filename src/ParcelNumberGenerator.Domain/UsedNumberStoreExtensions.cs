namespace ParcelNumberGenerator.Domain;

/// <summary>
/// Pool-shaped queries composed from the primitives on <see cref="IUsedNumberStore"/>.
/// </summary>
/// <remarks>
/// These live here rather than on the interface so that an implementation has three methods
/// to get right instead of five, and so the "skip the exclusions" logic exists once for
/// every store. It matters for correctness, not just tidiness: a number issued before an
/// exclusion was introduced still sits in the table inside that excluded window, so counting
/// over the whole outer range would report a pool as fuller than it is — and, at the
/// boundary, as exhausted while it still has free numbers.
/// </remarks>
public static class UsedNumberStoreExtensions
{
    /// <summary>
    /// How many of the pool's <em>allocatable</em> numbers have been issued. Numbers sitting
    /// inside an exclusion are not counted, because they can never be issued again.
    /// </summary>
    public static async Task<long> CountUsedInPoolAsync(
        this IUsedNumberStore store,
        NumberPool pool,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(store);
        ArgumentNullException.ThrowIfNull(pool);

        long total = 0;
        foreach (NumberRange segment in pool.Segments)
        {
            total += await store.CountUsedAsync(segment, cancellationToken).ConfigureAwait(false);
        }

        return total;
    }

    /// <summary>
    /// Every issued allocatable number in the pool, ascending, streamed segment by segment.
    /// </summary>
    public static async IAsyncEnumerable<int> StreamUsedInPoolAsync(
        this IUsedNumberStore store,
        NumberPool pool,
        [System.Runtime.CompilerServices.EnumeratorCancellation] CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(store);
        ArgumentNullException.ThrowIfNull(pool);

        foreach (NumberRange segment in pool.Segments)
        {
            await foreach (int number in store.StreamUsedAsync(segment, cancellationToken).ConfigureAwait(false))
            {
                yield return number;
            }
        }
    }
}
