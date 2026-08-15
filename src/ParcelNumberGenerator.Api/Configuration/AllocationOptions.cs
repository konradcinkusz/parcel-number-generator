using System.ComponentModel.DataAnnotations;
using ParcelNumberGenerator.Domain.Allocation;

namespace ParcelNumberGenerator.Api.Configuration;

/// <summary>
/// How numbers are drawn, bound from the <c>Allocation</c> configuration section.
/// </summary>
public sealed class AllocationOptions
{
    public const string SectionName = "Allocation";

    /// <summary>
    /// Which <see cref="IAllocationStrategy"/> to use, by its
    /// <see cref="IAllocationStrategy.Name"/>. Unknown values fail at startup, listing the
    /// names that do exist.
    /// </summary>
    public string Strategy { get; init; } = AdaptiveAllocationStrategy.StrategyName;

    /// <summary>Claims a single allocation may make before giving up. 0 = strategy default.</summary>
    [Range(0, 1024)]
    public int MaxAttempts { get; init; }

    /// <summary>
    /// Ceiling on <c>count</c> for a batch request. A batch is a loop of individual claims,
    /// so an unbounded count is an unbounded request — the one thing a public endpoint must
    /// not offer.
    /// </summary>
    [Range(1, 10_000)]
    public int MaxBatchSize { get; init; } = 1_000;
}
