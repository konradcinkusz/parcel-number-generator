using System.ComponentModel.DataAnnotations;

namespace ParcelNumberGenerator.Contracts;

/// <summary>
/// Raise a notification. <see cref="ParcelNumber"/> accepts any upstream dialect — the
/// service normalizes it at the edge (P11) and stores the canonical form.
/// </summary>
public sealed record RaiseNotificationRequest
{
    /// <summary>
    /// The parcel this notification is about, in any accepted dialect, or null for a
    /// notification that is not parcel-scoped (a shift announcement, a system message).
    /// </summary>
    [MaxLength(64)]
    public string? ParcelNumber { get; init; }

    [Required]
    [MaxLength(NotificationLimits.MaxBodyLength)]
    public required string Body { get; init; }

    public NotificationSeverity Severity { get; init; } = NotificationSeverity.Unspecified;

    public ParcelEventKind RaisedBy { get; init; } = ParcelEventKind.Manual;

    /// <summary>
    /// Whether an operator has to acknowledge this before it stops being outstanding.
    /// </summary>
    public bool AcknowledgementRequired { get; init; }

    /// <summary>Keep this notification at the top of the operator's list.</summary>
    public bool Pinned { get; init; }
}

/// <summary>Amend a notification that has already been raised.</summary>
public sealed record UpdateNotificationRequest
{
    [Required]
    [MaxLength(NotificationLimits.MaxBodyLength)]
    public required string Body { get; init; }

    public NotificationSeverity Severity { get; init; }

    public bool AcknowledgementRequired { get; init; }

    public bool Pinned { get; init; }
}

public sealed record NotificationResponse
{
    public required Guid Id { get; init; }

    /// <summary>Canonical parcel number, or null when the notification is not parcel-scoped.</summary>
    public string? ParcelNumber { get; init; }

    public required string Body { get; init; }

    public required NotificationSeverity Severity { get; init; }

    public required ParcelEventKind RaisedBy { get; init; }

    public required bool AcknowledgementRequired { get; init; }

    /// <summary>
    /// When the notification was acknowledged, or null if it has not been. The legacy
    /// model carried a bare <c>Confirmed</c> boolean, which could not answer "when" or
    /// "by whom" — the two questions actually asked when a parcel goes missing.
    /// </summary>
    public DateTimeOffset? AcknowledgedAt { get; init; }

    public string? AcknowledgedBy { get; init; }

    /// <summary>True when acknowledgement is required and has not happened yet.</summary>
    public required bool IsOutstanding { get; init; }

    public required bool Pinned { get; init; }

    public required DateTimeOffset CreatedAt { get; init; }
}

/// <summary>One page of notifications, plus the counts an operator dashboard needs.</summary>
public sealed record NotificationPageResponse
{
    public required IReadOnlyList<NotificationResponse> Items { get; init; }

    public required int Page { get; init; }

    public required int Limit { get; init; }

    public required int Total { get; init; }

    /// <summary>Notifications awaiting acknowledgement across the whole filtered set, not just this page.</summary>
    public required int Outstanding { get; init; }
}

/// <summary>
/// Validation limits, published so a client can mirror them deliberately
/// (SERVICE-API-PATTERNS §3) rather than hard-coding a guess that drifts. The server
/// remains authoritative.
/// </summary>
public static class NotificationLimits
{
    public const int MaxBodyLength = 512;

    public const int MaxPageSize = 100;

    public const int DefaultPageSize = 25;
}
