using Microsoft.Extensions.Logging;
using ParcelNumberGenerator.Contracts;

namespace ParcelNumberGenerator.Notifications.Services;

/// <summary>
/// P8 — the working fallback. With no delivery integration configured, notifications are
/// still raised, still persisted and still readable over the API; they are additionally
/// written to the log rather than pushed anywhere. This is what makes
/// <c>git clone &amp;&amp; dotnet run</c> with zero credentials a working system.
/// </summary>
public sealed partial class LoggingNotificationChannel(ILogger<LoggingNotificationChannel> logger)
    : INotificationChannel
{
    public string Name => "log";

    public Task<NotificationDeliveryResult> DeliverAsync(
        NotificationResponse notification,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(notification);

        Raised(
            logger,
            notification.Severity,
            notification.ParcelNumber ?? "(none)",
            notification.RaisedBy,
            notification.Id);

        return Task.FromResult(NotificationDeliveryResult.Success(Name));
    }

    [LoggerMessage(
        EventId = 2000,
        Level = LogLevel.Information,
        Message = "Notification raised [{Severity}] parcel={ParcelNumber} event={RaisedBy} id={NotificationId}")]
    private static partial void Raised(
        ILogger logger,
        NotificationSeverity severity,
        string parcelNumber,
        ParcelEventKind raisedBy,
        Guid notificationId);
}
