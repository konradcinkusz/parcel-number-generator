using System.ComponentModel.DataAnnotations;
using ParcelNumberGenerator.Domain;

namespace ParcelNumberGenerator.Api.Configuration;

/// <summary>
/// The pool this deployment issues numbers from, bound from the <c>Pool</c> configuration
/// section.
/// </summary>
/// <remarks>
/// Configuration, not code. The legacy generators each hardcoded their own range in a
/// default constructor — <c>1..10,000,000</c> in three classes and <c>1..100,000</c> in a
/// fourth — so which pool you got depended on which implementation the caller happened to
/// construct, and changing it meant a rebuild.
/// </remarks>
public sealed class PoolOptions
{
    public const string SectionName = "Pool";

    [Range(0, int.MaxValue)]
    public int From { get; init; } = 1;

    [Range(1, int.MaxValue)]
    public int To { get; init; } = 9_999_999;

    /// <summary>
    /// Sub-ranges withheld from allocation — reserved blocks, a carrier's own prefixes, or
    /// numbers burned by an earlier system.
    /// </summary>
    public IList<RangeOptions> Exclusions { get; init; } = [];

    public NumberPool ToPool()
    {
        NumberRange range = new(From, To);
        return NumberPool.Create(range, Exclusions.Select(exclusion => exclusion.ToRange()));
    }

    /// <summary>
    /// Checks what data annotations cannot: that the ends are ordered and that the
    /// exclusions leave something to allocate.
    /// </summary>
    public IEnumerable<string> Validate()
    {
        if (From > To)
        {
            yield return $"{SectionName}:From ({From}) must not exceed {SectionName}:To ({To}).";
            yield break;
        }

        foreach (RangeOptions exclusion in Exclusions.Where(exclusion => exclusion.From > exclusion.To))
        {
            yield return $"{SectionName}:Exclusions entry [{exclusion.From}, {exclusion.To}] is inverted.";
        }

        if (Exclusions.All(exclusion => exclusion.From <= exclusion.To) && ToPool().Capacity == 0)
        {
            yield return $"{SectionName} exclusions remove every number in [{From}, {To}]; nothing could be allocated.";
        }
    }
}

public sealed class RangeOptions
{
    public int From { get; init; }

    public int To { get; init; }

    public NumberRange ToRange() => new(From, To);
}
