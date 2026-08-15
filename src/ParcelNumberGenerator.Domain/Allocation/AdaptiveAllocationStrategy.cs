namespace ParcelNumberGenerator.Domain.Allocation;

/// <summary>
/// Probes at random, and escalates to a scan when probing runs out of attempts. The default.
/// </summary>
/// <remarks>
/// <para>
/// Neither of the two strategies alone covers a pool's whole life. Random probing is one
/// round trip while the pool is mostly free and cannot finish a nearly-full one: drawing the
/// last of 50 numbers uniformly needs about 50 attempts, so a fixed budget of 16 gives up
/// short of the end and the pool can never be drained. Scanning finishes every time and is
/// wasteful for the 99% of a pool's life when it is not nearly full.
/// </para>
/// <para>
/// The escalation is free on the happy path, because it is triggered by the outcome rather
/// than by a density measurement: <see cref="AllocationStatus.Contended"/> already means
/// "free numbers exist but probing did not find one", which is exactly the condition a scan
/// resolves. Checking density up front would instead add a COUNT to every allocation,
/// including the overwhelming majority that succeed on the first probe.
/// </para>
/// <para>
/// Composition, not inheritance — this is a third <see cref="IAllocationStrategy"/> that
/// holds two others, and either can still be selected on its own by name.
/// </para>
/// </remarks>
public sealed class AdaptiveAllocationStrategy(
    RandomProbeAllocationStrategy randomProbe,
    SequentialScanAllocationStrategy sequentialScan) : IAllocationStrategy
{
    public const string StrategyName = "adaptive";

    public string Name => StrategyName;

    public async Task<AllocationResult> AllocateAsync(NumberPool pool, CancellationToken cancellationToken)
    {
        AllocationResult probed = await randomProbe.AllocateAsync(pool, cancellationToken).ConfigureAwait(false);

        // Exhausted means the pool is genuinely empty; a scan would only confirm it slower.
        if (probed.Status != AllocationStatus.Contended)
        {
            return probed;
        }

        return await sequentialScan.AllocateAsync(pool, cancellationToken).ConfigureAwait(false);
    }
}
