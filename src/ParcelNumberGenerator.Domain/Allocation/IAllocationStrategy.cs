namespace ParcelNumberGenerator.Domain.Allocation;

/// <summary>
/// Draws one unused number from a pool and claims it.
/// </summary>
/// <remarks>
/// <para>
/// The extension point of this service (P10). A new way of choosing a number is a class
/// implementing this interface plus one registration line — there is no base class to
/// derive from and no protected member to override.
/// </para>
/// <para>
/// This replaces six legacy generators that shared behaviour by inheriting it:
/// <c>NumberPoolDBv2</c> held the algorithm plus its own ADO.NET plumbing, and each variant
/// overrode a <c>protected virtual</c> hook, so a change to the search affected every
/// subclass and the database access could not be substituted at all.
/// </para>
/// </remarks>
public interface IAllocationStrategy
{
    /// <summary>
    /// The name this strategy is selected by, via <c>Allocation:Strategy</c> in
    /// configuration. Lowercase and stable — it is a configuration value, not a label.
    /// </summary>
    string Name { get; }

    Task<AllocationResult> AllocateAsync(NumberPool pool, CancellationToken cancellationToken);
}
