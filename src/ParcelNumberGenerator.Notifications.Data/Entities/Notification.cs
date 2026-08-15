using ParcelNumberGenerator.Contracts;

namespace ParcelNumberGenerator.Notifications.Data.Entities;

/// <summary>
/// A notification raised against the warehouse, optionally scoped to a parcel.
/// </summary>
/// <remarks>
/// The legacy <c>Message</c> entity this replaces had three defects that are fixed by the
/// shape here rather than by discipline:
/// <list type="bullet">
///   <item>its <c>Id</c> was chosen by the client from <c>GetLastId()</c>, so two
///   concurrent senders picked the same integer — the key is now database-assigned;</item>
///   <item><c>Confirmed</c> was a bare boolean, which cannot answer when or by whom —
///   replaced by <see cref="AcknowledgedAt"/> and <see cref="AcknowledgedBy"/>;</item>
///   <item>it carried no creation timestamp at all, so the list could only ever be
///   ordered by insertion key.</item>
/// </list>
/// </remarks>
public sealed class Notification
{
    public Guid Id { get; set; }

    /// <summary>
    /// The parcel this concerns, already normalized to canonical form by the edge, or
    /// null when the notification is not parcel-scoped. Never store an upstream dialect
    /// here — that is the whole point of the normalization (P11).
    /// </summary>
    public string? ParcelNumber { get; set; }

    public required string Body { get; set; }

    public NotificationSeverity Severity { get; set; } = NotificationSeverity.Unspecified;

    public ParcelEventKind RaisedBy { get; set; } = ParcelEventKind.Manual;

    public bool AcknowledgementRequired { get; set; }

    public DateTimeOffset? AcknowledgedAt { get; set; }

    public string? AcknowledgedBy { get; set; }

    public bool Pinned { get; set; }

    public DateTimeOffset CreatedAt { get; set; }

    /// <summary>
    /// Outstanding means "someone still has to look at this". Computed rather than
    /// stored, so it cannot disagree with the two columns it derives from.
    /// </summary>
    public bool IsOutstanding => AcknowledgementRequired && AcknowledgedAt is null;
}
