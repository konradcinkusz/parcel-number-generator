namespace ParcelNumberGenerator.Domain;

/// <summary>
/// A closed interval of parcel numbers — both ends are part of the range.
/// </summary>
/// <remarks>
/// Replaces the <c>Tuple&lt;int, int&gt;</c> the legacy generators passed around. A tuple
/// carries no invariant, so nothing stopped an inverted range being constructed and every
/// consumer re-derived "is this closed or half-open?" for itself — which is where the
/// off-by-one in the old <c>ElementsInRange</c> came from.
/// </remarks>
public readonly record struct NumberRange
{
    public NumberRange(int from, int to)
    {
        if (from > to)
        {
            throw new ArgumentException($"Range start {from} is greater than its end {to}.", nameof(from));
        }

        From = from;
        To = to;
    }

    /// <summary>First number in the range, inclusive.</summary>
    public int From { get; }

    /// <summary>Last number in the range, inclusive.</summary>
    public int To { get; }

    /// <summary>
    /// How many numbers the range holds. <see cref="long"/> because a full
    /// <see cref="int"/> range holds more values than an <see cref="int"/> can count.
    /// </summary>
    public long Count => (long)To - From + 1;

    public bool Contains(int value) => value >= From && value <= To;

    public bool Overlaps(NumberRange other) => From <= other.To && other.From <= To;

    /// <summary>
    /// The overlap between this range and <paramref name="other"/>, or <c>null</c> when
    /// they are disjoint.
    /// </summary>
    public NumberRange? Intersect(NumberRange other)
    {
        int from = Math.Max(From, other.From);
        int to = Math.Min(To, other.To);
        return from <= to ? new NumberRange(from, to) : null;
    }

    public override string ToString() => $"[{From}, {To}]";
}
