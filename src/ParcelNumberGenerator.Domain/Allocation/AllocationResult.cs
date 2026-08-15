namespace ParcelNumberGenerator.Domain.Allocation;

public enum AllocationStatus
{
    /// <summary>A number was drawn and durably claimed.</summary>
    Allocated,

    /// <summary>Every number in the pool has been issued. Widen the pool or stop asking.</summary>
    PoolExhausted,

    /// <summary>
    /// The pool has free numbers but the strategy kept losing the race for them within its
    /// attempt budget. Retryable, unlike <see cref="PoolExhausted"/>.
    /// </summary>
    Contended,
}

/// <summary>
/// The outcome of one allocation.
/// </summary>
/// <remarks>
/// A result type rather than an exception per outcome. The legacy generators threw
/// <c>new Exception("Empty pool...")</c> for exhaustion, <c>new Exception("Something went
/// wrong with database")</c> for an inconsistent count, and returned <c>-1</c> from one
/// branch of the chain-of-responsibility variant — so a caller could not tell "stop asking"
/// from "try again" without matching on message strings.
/// </remarks>
public readonly record struct AllocationResult
{
    private AllocationResult(AllocationStatus status, int number, int attempts)
    {
        Status = status;
        Number = number;
        Attempts = attempts;
    }

    public AllocationStatus Status { get; }

    /// <summary>The issued number. Only meaningful when <see cref="Status"/> is Allocated.</summary>
    public int Number { get; }

    /// <summary>How many claims the strategy made. Exported as a metric; useful for tuning.</summary>
    public int Attempts { get; }

    public bool IsSuccess => Status == AllocationStatus.Allocated;

    public static AllocationResult Allocated(int number, int attempts) =>
        new(AllocationStatus.Allocated, number, attempts);

    public static AllocationResult PoolExhausted() =>
        new(AllocationStatus.PoolExhausted, default, 0);

    public static AllocationResult Contended(int attempts) =>
        new(AllocationStatus.Contended, default, attempts);
}
