namespace ParcelNumberGenerator.Domain;

/// <summary>
/// The set of parcel numbers a service is allowed to issue: one outer range, minus any
/// number of excluded sub-ranges.
/// </summary>
/// <remarks>
/// <para>
/// The pool is normalized once, at construction, into a list of disjoint ascending
/// <em>segments</em> of allocatable numbers, plus a prefix sum of their sizes. That gives
/// two operations the allocation strategies are built on, both O(log segments) and both
/// free of any I/O:
/// </para>
/// <list type="bullet">
///   <item><see cref="NumberAt"/> — the i-th allocatable number, counting from zero.</item>
///   <item><see cref="IndexOf"/> — the inverse.</item>
/// </list>
/// <para>
/// Exclusions therefore cost nothing at allocation time. The legacy
/// <c>NumberPoolDBv2WithRangeOff</c> instead special-cased the excluded window inside its
/// binary search, where it compared a row <em>index</em> against a number <em>value</em>
/// and skipped the wrong rows.
/// </para>
/// </remarks>
public sealed class NumberPool
{
    private readonly NumberRange[] _segments;

    /// <summary>
    /// <c>_offsets[i]</c> is how many allocatable numbers precede <c>_segments[i]</c>.
    /// Ascending, so it can be binary-searched by allocation index.
    /// </summary>
    private readonly long[] _offsets;

    private NumberPool(NumberRange range, NumberRange[] exclusions, NumberRange[] segments)
    {
        Range = range;
        Exclusions = exclusions;
        _segments = segments;

        _offsets = new long[segments.Length];
        long running = 0;
        for (int i = 0; i < segments.Length; i++)
        {
            _offsets[i] = running;
            running += segments[i].Count;
        }

        Capacity = running;
    }

    /// <summary>The outer range, before exclusions.</summary>
    public NumberRange Range { get; }

    /// <summary>
    /// The excluded ranges, normalized: clipped to <see cref="Range"/>, sorted ascending and
    /// merged, so overlapping or adjacent inputs collapse into one entry.
    /// </summary>
    public IReadOnlyList<NumberRange> Exclusions { get; }

    /// <summary>How many numbers the pool can ever issue, exclusions already removed.</summary>
    public long Capacity { get; }

    /// <summary>The allocatable sub-ranges, ascending and disjoint.</summary>
    public IReadOnlyList<NumberRange> Segments => _segments;

    public static NumberPool Create(NumberRange range, IEnumerable<NumberRange>? exclusions = null)
    {
        NumberRange[] merged = Normalize(range, exclusions);
        return new NumberPool(range, merged, Subtract(range, merged));
    }

    /// <summary>Whether <paramref name="number"/> is inside the pool and not excluded.</summary>
    public bool Contains(int number) => IndexOf(number) is not null;

    /// <summary>
    /// The allocatable number at zero-based <paramref name="index"/>, skipping exclusions.
    /// </summary>
    /// <exception cref="ArgumentOutOfRangeException">
    /// <paramref name="index"/> is negative or not below <see cref="Capacity"/>.
    /// </exception>
    public int NumberAt(long index)
    {
        ArgumentOutOfRangeException.ThrowIfNegative(index);
        ArgumentOutOfRangeException.ThrowIfGreaterThanOrEqual(index, Capacity);

        int segment = FindSegmentByOffset(index);
        return (int)(_segments[segment].From + (index - _offsets[segment]));
    }

    /// <summary>
    /// The zero-based allocation index of <paramref name="number"/>, or <c>null</c> when it
    /// falls outside the pool or inside an exclusion.
    /// </summary>
    public long? IndexOf(int number)
    {
        int segment = FindSegmentByNumber(number);
        if (segment < 0 || !_segments[segment].Contains(number))
        {
            return null;
        }

        return _offsets[segment] + (number - _segments[segment].From);
    }

    /// <summary>Greatest <c>i</c> with <c>_offsets[i] &lt;= index</c>.</summary>
    private int FindSegmentByOffset(long index)
    {
        int low = 0;
        int high = _segments.Length - 1;
        while (low < high)
        {
            int mid = low + ((high - low + 1) / 2);
            if (_offsets[mid] <= index)
            {
                low = mid;
            }
            else
            {
                high = mid - 1;
            }
        }

        return low;
    }

    /// <summary>Greatest <c>i</c> with <c>_segments[i].From &lt;= number</c>, or -1.</summary>
    private int FindSegmentByNumber(int number)
    {
        int low = 0;
        int high = _segments.Length - 1;
        int found = -1;
        while (low <= high)
        {
            int mid = low + ((high - low) / 2);
            if (_segments[mid].From <= number)
            {
                found = mid;
                low = mid + 1;
            }
            else
            {
                high = mid - 1;
            }
        }

        return found;
    }

    /// <summary>Clip exclusions to the pool range, then sort and merge them.</summary>
    private static NumberRange[] Normalize(NumberRange range, IEnumerable<NumberRange>? exclusions)
    {
        if (exclusions is null)
        {
            return [];
        }

        List<NumberRange> clipped = [];
        foreach (NumberRange exclusion in exclusions)
        {
            if (range.Intersect(exclusion) is { } overlap)
            {
                clipped.Add(overlap);
            }
        }

        if (clipped.Count == 0)
        {
            return [];
        }

        clipped.Sort(static (left, right) => left.From.CompareTo(right.From));

        List<NumberRange> merged = [];
        NumberRange current = clipped[0];
        foreach (NumberRange next in clipped.Skip(1))
        {
            // Merge on touching as well as overlapping: [1,5] and [6,9] leave no gap
            // between them, so keeping them separate would only add a segment boundary.
            // `current.To + 1` cannot overflow — an exclusion clipped to the pool range
            // ends at or below range.To, which is at most int.MaxValue, and the branch is
            // only reached when next.From is strictly greater.
            if (next.From <= current.To || (current.To < int.MaxValue && next.From == current.To + 1))
            {
                current = new NumberRange(current.From, Math.Max(current.To, next.To));
            }
            else
            {
                merged.Add(current);
                current = next;
            }
        }

        merged.Add(current);
        return [.. merged];
    }

    /// <summary>The parts of <paramref name="range"/> left over after removing exclusions.</summary>
    private static NumberRange[] Subtract(NumberRange range, NumberRange[] exclusions)
    {
        List<NumberRange> segments = [];
        long cursor = range.From;

        foreach (NumberRange exclusion in exclusions)
        {
            if (cursor <= exclusion.From - 1L)
            {
                segments.Add(new NumberRange((int)cursor, exclusion.From - 1));
            }

            // A long cursor keeps an exclusion ending at int.MaxValue from wrapping.
            cursor = Math.Max(cursor, exclusion.To + 1L);
        }

        if (cursor <= range.To)
        {
            segments.Add(new NumberRange((int)cursor, range.To));
        }

        return [.. segments];
    }
}
