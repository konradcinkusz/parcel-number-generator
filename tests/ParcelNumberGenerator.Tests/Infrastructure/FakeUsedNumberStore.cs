using ParcelNumberGenerator.Domain;

namespace ParcelNumberGenerator.Tests.Infrastructure;

/// <summary>
/// An in-memory <see cref="IUsedNumberStore"/> whose reservation is atomic, so a strategy
/// can be tested against the same guarantee the database gives it.
/// </summary>
public sealed class FakeUsedNumberStore : IUsedNumberStore
{
    private readonly SortedSet<int> _used = [];
    private readonly Lock _gate = new();

    /// <summary>
    /// Runs before each reservation. Use it to have a "concurrent caller" take the number
    /// this one is about to claim, which is the race the real store has to survive.
    /// </summary>
    public Action<int>? BeforeReserve { get; set; }

    public int ReserveAttempts { get; private set; }

    public IReadOnlyCollection<int> Used
    {
        get
        {
            lock (_gate)
            {
                return [.. _used];
            }
        }
    }

    public void Seed(params IEnumerable<int> numbers)
    {
        lock (_gate)
        {
            foreach (int number in numbers)
            {
                _used.Add(number);
            }
        }
    }

    public Task<bool> TryReserveAsync(int number, CancellationToken cancellationToken)
    {
        BeforeReserve?.Invoke(number);

        lock (_gate)
        {
            ReserveAttempts++;
            return Task.FromResult(_used.Add(number));
        }
    }

    public Task<bool> IsUsedAsync(int number, CancellationToken cancellationToken)
    {
        lock (_gate)
        {
            return Task.FromResult(_used.Contains(number));
        }
    }

    public Task<long> CountUsedAsync(NumberRange range, CancellationToken cancellationToken)
    {
        lock (_gate)
        {
            return Task.FromResult(_used.LongCount(range.Contains));
        }
    }

    public async IAsyncEnumerable<int> StreamUsedAsync(
        NumberRange range,
        [System.Runtime.CompilerServices.EnumeratorCancellation] CancellationToken cancellationToken)
    {
        int[] snapshot;
        lock (_gate)
        {
            snapshot = [.. _used.Where(range.Contains)];
        }

        foreach (int number in snapshot)
        {
            cancellationToken.ThrowIfCancellationRequested();
            yield return number;
            await Task.Yield();
        }
    }
}
