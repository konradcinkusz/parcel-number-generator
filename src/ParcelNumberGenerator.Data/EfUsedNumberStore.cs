using System.Runtime.CompilerServices;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.ChangeTracking;
using ParcelNumberGenerator.Domain;

namespace ParcelNumberGenerator.Data;

/// <summary>
/// <see cref="IUsedNumberStore"/> over Entity Framework Core.
/// </summary>
public sealed class EfUsedNumberStore(ParcelNumbersDbContext db, TimeProvider timeProvider) : IUsedNumberStore
{
    /// <summary>
    /// Claims a number by inserting it and letting the primary key arbitrate.
    /// </summary>
    /// <remarks>
    /// <para>
    /// Insert-and-catch, not check-then-insert. Two callers drawing the same candidate both
    /// reach the insert; exactly one commits, and the other is told it lost. There is no
    /// window between the check and the write for them to interleave in, because there is no
    /// check.
    /// </para>
    /// <para>
    /// The failure is confirmed by re-reading rather than by matching a provider's error
    /// code. That keeps the store portable across PostgreSQL, SQL Server and the in-memory
    /// provider — which report a duplicate key three different ways — and, more usefully, it
    /// cannot silently swallow an unrelated write failure: if the row is not there
    /// afterwards, the original exception is rethrown.
    /// </para>
    /// </remarks>
    public async Task<bool> TryReserveAsync(int number, CancellationToken cancellationToken)
    {
        UsedNumber entity = new()
        {
            Number = number,
            AllocatedAtUtc = timeProvider.GetUtcNow(),
        };

        try
        {
            // Add is inside the try because not every provider rejects a duplicate at the
            // same moment: a relational one fails at SaveChanges with a DbUpdateException,
            // while the in-memory provider throws right here if the context already tracks
            // that key.
            db.UsedNumbers.Add(entity);
            await db.SaveChangesAsync(cancellationToken).ConfigureAwait(false);
            return true;
        }
        // Three exception types because the three supported providers genuinely report a
        // duplicate three different ways — DbUpdateException from a relational provider,
        // InvalidOperationException from the in-memory provider's change tracker, and a bare
        // ArgumentException from its table writer. Catching this broadly is only safe
        // because of the re-read below: anything that is not actually a duplicate is
        // rethrown untouched.
        catch (Exception ex) when (ex is DbUpdateException or InvalidOperationException or ArgumentException)
        {
            if (await IsUsedAsync(number, cancellationToken).ConfigureAwait(false))
            {
                return false;
            }

            throw;
        }
        finally
        {
            // Nothing needs the entity after the write. Detaching keeps a batch of a thousand
            // allocations from accumulating a thousand tracked entities in one scoped
            // context — and, on the failure path, stops a rejected insert being resubmitted
            // by the next SaveChanges, where it would fail an allocation that has nothing to
            // do with this number.
            EntityEntry<UsedNumber> entry = db.Entry(entity);
            if (entry.State != EntityState.Detached)
            {
                entry.State = EntityState.Detached;
            }
        }
    }

    public Task<bool> IsUsedAsync(int number, CancellationToken cancellationToken) =>
        db.UsedNumbers.AsNoTracking().AnyAsync(used => used.Number == number, cancellationToken);

    public async Task<long> CountUsedAsync(NumberRange range, CancellationToken cancellationToken) =>
        await db.UsedNumbers
            .AsNoTracking()
            .Where(used => used.Number >= range.From && used.Number <= range.To)
            .LongCountAsync(cancellationToken)
            .ConfigureAwait(false);

    /// <remarks>
    /// An iterator rather than a bare <c>AsAsyncEnumerable()</c> so the token is bound to the
    /// enumeration here, instead of depending on every caller remembering
    /// <c>WithCancellation</c>. A scan over a nearly-full pool is the longest-running query
    /// this service issues, so it is the one that most needs to be interruptible.
    /// </remarks>
    public async IAsyncEnumerable<int> StreamUsedAsync(
        NumberRange range,
        [EnumeratorCancellation] CancellationToken cancellationToken)
    {
        IAsyncEnumerable<int> query = db.UsedNumbers
            .AsNoTracking()
            .Where(used => used.Number >= range.From && used.Number <= range.To)
            .OrderBy(used => used.Number)
            .Select(used => used.Number)
            .AsAsyncEnumerable();

        await foreach (int number in query.WithCancellation(cancellationToken).ConfigureAwait(false))
        {
            yield return number;
        }
    }
}
