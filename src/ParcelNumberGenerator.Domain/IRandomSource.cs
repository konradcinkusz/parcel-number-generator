namespace ParcelNumberGenerator.Domain;

/// <summary>
/// Supplies the randomness an allocation strategy draws from.
/// </summary>
/// <remarks>
/// A seam, so a strategy can be tested against a known sequence instead of by running it
/// enough times to be confident. The legacy code constructed <c>new Random()</c> inside the
/// draw itself, which on .NET Framework seeds from a low-resolution clock: a loop calling it
/// tightly got the same seed and therefore the same number repeatedly.
/// </remarks>
public interface IRandomSource
{
    /// <summary>A uniformly distributed value in <c>[0, exclusiveUpperBound)</c>.</summary>
    long NextInt64(long exclusiveUpperBound);
}

/// <summary>Backed by <see cref="Random.Shared"/>: thread-safe and seeded per process.</summary>
public sealed class SharedRandomSource : IRandomSource
{
    public long NextInt64(long exclusiveUpperBound)
    {
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(exclusiveUpperBound);
        return Random.Shared.NextInt64(exclusiveUpperBound);
    }
}
