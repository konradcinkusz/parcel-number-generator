namespace ParcelNumberGenerator.Domain;

/// <summary>
/// The only way the domain reaches persistence. Implemented in
/// <c>ParcelNumberGenerator.Data</c>; the domain never sees a <c>DbContext</c>.
/// </summary>
public interface IUsedNumberStore
{
    /// <summary>
    /// Claims <paramref name="number"/> for the caller.
    /// </summary>
    /// <returns>
    /// <c>true</c> when this call is the one that claimed it; <c>false</c> when it was
    /// already taken.
    /// </returns>
    /// <remarks>
    /// This is the concurrency boundary of the whole system, and the reason it returns a
    /// <see cref="bool"/> instead of taking a "check, then insert" pair. The check-then-act
    /// shape the legacy code used — search the table, then insert if absent — issues the
    /// same number to two callers whose requests interleave between the two statements,
    /// with nothing in the schema to stop it. Implementations must make the claim atomic
    /// and report the loss rather than throwing.
    /// </remarks>
    Task<bool> TryReserveAsync(int number, CancellationToken cancellationToken);

    /// <summary>Whether <paramref name="number"/> has already been issued.</summary>
    Task<bool> IsUsedAsync(int number, CancellationToken cancellationToken);

    /// <summary>How many numbers inside <paramref name="range"/> have been issued.</summary>
    Task<long> CountUsedAsync(NumberRange range, CancellationToken cancellationToken);

    /// <summary>
    /// Every issued number inside <paramref name="range"/>, ascending.
    /// </summary>
    /// <remarks>
    /// Streamed rather than returned as a list: the whole point of using it is that the
    /// pool is nearly full, which is exactly when materializing it would be worst.
    /// </remarks>
    IAsyncEnumerable<int> StreamUsedAsync(NumberRange range, CancellationToken cancellationToken);
}
