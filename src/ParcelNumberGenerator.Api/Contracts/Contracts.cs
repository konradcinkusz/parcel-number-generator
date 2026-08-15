namespace ParcelNumberGenerator.Api.Contracts;

/// <summary>The result of an allocation request.</summary>
/// <param name="Numbers">The numbers issued, in the order they were drawn.</param>
/// <param name="Requested">How many were asked for.</param>
/// <param name="Complete">
/// Whether the full request was satisfied. When false, <paramref name="Numbers"/> still
/// lists what was issued — those numbers are claimed and will not be issued again.
/// </param>
/// <param name="Reason">Why the request stopped short, or <c>null</c> when it did not.</param>
public sealed record AllocationResponse(
    IReadOnlyList<int> Numbers,
    int Requested,
    bool Complete,
    string? Reason);

/// <param name="Number">The number asked about.</param>
/// <param name="Used">Whether it has been issued.</param>
/// <param name="InPool">
/// Whether it is allocatable at all — false for a number outside the configured range or
/// inside an exclusion.
/// </param>
public sealed record NumberStatusResponse(int Number, bool Used, bool InPool);

/// <summary>What the pool holds and how much of it is left.</summary>
public sealed record PoolResponse(
    int From,
    int To,
    IReadOnlyList<ExcludedRange> Exclusions,
    long Capacity,
    long Used,
    long Remaining,
    double Density,
    string Strategy);

public sealed record ExcludedRange(int From, int To);
