namespace ParcelNumberGenerator.Data;

/// <summary>
/// One issued parcel number. The table is a claim ledger, not a sequence.
/// </summary>
/// <remarks>
/// The number itself is the primary key, which is the whole concurrency design in one line:
/// the uniqueness that stops a number being issued twice is enforced by the database, on
/// every writer, including one that bypasses this application. The legacy schema —
/// <c>create table USED_NUMBERS (usedNumber INT)</c>, no key, no index — enforced nothing,
/// and the EF6 model that replaced it added a surrogate <c>Id</c> and left the number
/// unconstrained, so duplicates stayed representable.
/// </remarks>
public sealed class UsedNumber
{
    public required int Number { get; init; }

    public required DateTimeOffset AllocatedAtUtc { get; init; }
}
