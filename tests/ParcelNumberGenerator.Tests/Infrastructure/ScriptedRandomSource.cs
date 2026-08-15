using ParcelNumberGenerator.Domain;

namespace ParcelNumberGenerator.Tests.Infrastructure;

/// <summary>
/// Returns a scripted sequence of draws, so a test can state exactly which candidates a
/// strategy will try instead of running it enough times to be reasonably sure.
/// </summary>
public sealed class ScriptedRandomSource(params long[] values) : IRandomSource
{
    private int _position;

    public long NextInt64(long exclusiveUpperBound)
    {
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(exclusiveUpperBound);

        if (_position >= values.Length)
        {
            throw new InvalidOperationException(
                $"The strategy asked for draw {_position + 1} but only {values.Length} were scripted.");
        }

        long value = values[_position++];
        Assert.InRange(value, 0, exclusiveUpperBound - 1);
        return value;
    }
}
