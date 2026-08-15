using Microsoft.EntityFrameworkCore;
using ParcelNumberGenerator.Contracts;
using ParcelNumberGenerator.Notifications.Data;
using ParcelNumberGenerator.Notifications.Data.Entities;
using ParcelNumberGenerator.Notifications.Domain;

namespace ParcelNumberGenerator.Notifications.Services;

/// <summary>
/// Use-case coordination for notifications (P9's middle layer). Endpoints bind and
/// delegate; this type owns the rules; the DbContext owns data access.
/// </summary>
public sealed class NotificationService(
    NotificationsDbContext dbContext,
    IEnumerable<INotificationChannel> channels,
    TimeProvider timeProvider)
{
    public async Task<RaiseOutcome> RaiseAsync(
        RaiseNotificationRequest request,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(request);

        // P11: the dialect stops here. Everything past this line sees canonical form.
        if (!ParcelNumber.TryParseOptional(request.ParcelNumber, out var canonicalParcelNumber))
        {
            return RaiseOutcome.Rejected(
                $"'{request.ParcelNumber}' is not a recognized parcel number. Expected the canonical "
                + "PNG-12345678-5 form, a bare 8- or 9-digit scan, or the legacy WMS/12345678 form.");
        }

        var notification = new Notification
        {
            Id = Guid.CreateVersion7(),
            ParcelNumber = canonicalParcelNumber,
            Body = request.Body,
            Severity = request.Severity,
            RaisedBy = request.RaisedBy,
            AcknowledgementRequired = request.AcknowledgementRequired,
            Pinned = request.Pinned,
            CreatedAt = timeProvider.GetUtcNow(),
        };

        dbContext.Notifications.Add(notification);
        await dbContext.SaveChangesAsync(cancellationToken);

        var response = ToResponse(notification);

        // Persist first, then fan out. A channel that is down must not lose the
        // notification, and the delivery results are advisory (P8).
        var deliveries = new List<NotificationDeliveryResult>();

        foreach (var channel in channels)
        {
            deliveries.Add(await channel.DeliverAsync(response, cancellationToken));
        }

        return RaiseOutcome.Raised(response, deliveries);
    }

    public async Task<NotificationPageResponse> GetPageAsync(
        NotificationQuery query,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(query);

        var baseQuery = dbContext.Notifications.AsNoTracking();

        if (query.ParcelNumber is { } parcelNumber)
        {
            baseQuery = baseQuery.Where(notification => notification.ParcelNumber == parcelNumber);
        }

        if (query.OutstandingOnly)
        {
            baseQuery = baseQuery.Where(notification =>
                notification.AcknowledgementRequired && notification.AcknowledgedAt == null);
        }

        if (query.Severity is { } severity)
        {
            baseQuery = baseQuery.Where(notification => notification.Severity == severity);
        }

        // SERVICE-API-PATTERNS §4: every count for the page in one round trip, computed
        // from the Include-free base query — not one Count() per statistic.
        var totals = await baseQuery
            .GroupBy(_ => 1)
            .Select(group => new
            {
                Total = group.Count(),
                Outstanding = group.Count(notification =>
                    notification.AcknowledgementRequired && notification.AcknowledgedAt == null),
            })
            .FirstOrDefaultAsync(cancellationToken);

        var items = await baseQuery
            .OrderByDescending(notification => notification.Pinned)
            .ThenByDescending(notification => notification.CreatedAt)
            .ThenBy(notification => notification.Id)
            .Skip((query.Page - 1) * query.Limit)
            .Take(query.Limit)
            .ToListAsync(cancellationToken);

        return new NotificationPageResponse
        {
            Items = [.. items.Select(ToResponse)],
            Page = query.Page,
            Limit = query.Limit,
            Total = totals?.Total ?? 0,
            Outstanding = totals?.Outstanding ?? 0,
        };
    }

    public async Task<NotificationResponse?> GetAsync(Guid id, CancellationToken cancellationToken)
    {
        var notification = await dbContext.Notifications
            .AsNoTracking()
            .SingleOrDefaultAsync(candidate => candidate.Id == id, cancellationToken);

        return notification is null ? null : ToResponse(notification);
    }

    /// <summary>
    /// Amends a notification in place.
    /// </summary>
    /// <remarks>
    /// The legacy implementation deleted the row and re-inserted it, which changed the
    /// primary key on every edit, silently dropped any column the copy constructor did
    /// not carry, and left a window where the row did not exist at all. This updates.
    /// </remarks>
    public async Task<NotificationResponse?> UpdateAsync(
        Guid id,
        UpdateNotificationRequest request,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(request);

        var notification = await dbContext.Notifications
            .SingleOrDefaultAsync(candidate => candidate.Id == id, cancellationToken);

        if (notification is null)
        {
            return null;
        }

        notification.Body = request.Body;
        notification.Severity = request.Severity;
        notification.AcknowledgementRequired = request.AcknowledgementRequired;
        notification.Pinned = request.Pinned;

        await dbContext.SaveChangesAsync(cancellationToken);

        return ToResponse(notification);
    }

    /// <summary>
    /// Records an acknowledgement. Idempotent: acknowledging twice keeps the first
    /// timestamp, because the question the record answers is "when was this first seen".
    /// </summary>
    public async Task<NotificationResponse?> AcknowledgeAsync(
        Guid id,
        string acknowledgedBy,
        CancellationToken cancellationToken)
    {
        var notification = await dbContext.Notifications
            .SingleOrDefaultAsync(candidate => candidate.Id == id, cancellationToken);

        if (notification is null)
        {
            return null;
        }

        if (notification.AcknowledgedAt is null)
        {
            notification.AcknowledgedAt = timeProvider.GetUtcNow();
            notification.AcknowledgedBy = acknowledgedBy;

            await dbContext.SaveChangesAsync(cancellationToken);
        }

        return ToResponse(notification);
    }

    public async Task<bool> DeleteAsync(Guid id, CancellationToken cancellationToken)
    {
        var deleted = await dbContext.Notifications
            .Where(candidate => candidate.Id == id)
            .ExecuteDeleteAsync(cancellationToken);

        return deleted > 0;
    }

    internal static NotificationResponse ToResponse(Notification notification) => new()
    {
        Id = notification.Id,
        ParcelNumber = notification.ParcelNumber,
        Body = notification.Body,
        Severity = notification.Severity,
        RaisedBy = notification.RaisedBy,
        AcknowledgementRequired = notification.AcknowledgementRequired,
        AcknowledgedAt = notification.AcknowledgedAt,
        AcknowledgedBy = notification.AcknowledgedBy,
        IsOutstanding = notification.IsOutstanding,
        Pinned = notification.Pinned,
        CreatedAt = notification.CreatedAt,
    };
}

/// <summary>
/// A validated list query. Page and limit are clamped on construction, so no endpoint can
/// forget to do it — an unclamped <c>limit=2000000</c> is a one-line outage
/// (SERVICE-API-PATTERNS §4).
/// </summary>
public sealed record NotificationQuery
{
    private NotificationQuery(
        int page,
        int limit,
        string? parcelNumber,
        bool outstandingOnly,
        NotificationSeverity? severity)
    {
        Page = page;
        Limit = limit;
        ParcelNumber = parcelNumber;
        OutstandingOnly = outstandingOnly;
        Severity = severity;
    }

    public int Page { get; }

    public int Limit { get; }

    /// <summary>Canonical form, or null for "any parcel".</summary>
    public string? ParcelNumber { get; }

    public bool OutstandingOnly { get; }

    public NotificationSeverity? Severity { get; }

    /// <summary>
    /// Builds a clamped query. Returns false only when a supplied parcel-number filter
    /// cannot be normalized — an out-of-range page or limit is clamped, not rejected,
    /// because a client asking for page 0 wants the first page.
    /// </summary>
    public static bool TryCreate(
        int? page,
        int? limit,
        string? parcelNumber,
        bool outstandingOnly,
        NotificationSeverity? severity,
        out NotificationQuery query)
    {
        query = default!;

        // Qualified: the property of the same name on this type would otherwise win the
        // lookup over the domain type.
        if (!Domain.ParcelNumber.TryParseOptional(parcelNumber, out var canonical))
        {
            return false;
        }

        query = new NotificationQuery(
            page: Math.Max(1, page ?? 1),
            limit: Math.Clamp(limit ?? NotificationLimits.DefaultPageSize, 1, NotificationLimits.MaxPageSize),
            parcelNumber: canonical,
            outstandingOnly: outstandingOnly,
            severity: severity);

        return true;
    }
}

/// <summary>The result of a raise: either the persisted notification, or why it was rejected.</summary>
public sealed record RaiseOutcome
{
    private RaiseOutcome(
        NotificationResponse? notification,
        IReadOnlyList<NotificationDeliveryResult> deliveries,
        string? rejection)
    {
        Notification = notification;
        Deliveries = deliveries;
        Rejection = rejection;
    }

    public NotificationResponse? Notification { get; }

    public IReadOnlyList<NotificationDeliveryResult> Deliveries { get; }

    public string? Rejection { get; }

    public bool IsRejected => Rejection is not null;

    public static RaiseOutcome Raised(
        NotificationResponse notification,
        IReadOnlyList<NotificationDeliveryResult> deliveries) => new(notification, deliveries, rejection: null);

    public static RaiseOutcome Rejected(string reason) => new(notification: null, [], reason);
}
