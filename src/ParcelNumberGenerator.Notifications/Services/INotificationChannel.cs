using ParcelNumberGenerator.Contracts;

namespace ParcelNumberGenerator.Notifications.Services;

/// <summary>
/// Where a raised notification is delivered. P10 — a new channel is a class implementing
/// this interface plus one DI registration; there is no base class to derive from.
/// </summary>
/// <remarks>
/// This replaces the legacy <c>IReceiver</c>, whose only implementation called
/// <c>MessageBox.Show</c> from inside a class library. That made delivery synchronously
/// blocking, untestable, and impossible to run anywhere without an interactive desktop
/// session — which is every environment this service now targets.
/// </remarks>
public interface INotificationChannel
{
    /// <summary>A stable name, used in logs and health reporting.</summary>
    string Name { get; }

    /// <summary>
    /// Delivers the notification. Implementations must not throw for a delivery failure
    /// they can describe — return the failure instead, so one broken channel does not
    /// take down the raise that fanned out to it.
    /// </summary>
    Task<NotificationDeliveryResult> DeliverAsync(
        NotificationResponse notification,
        CancellationToken cancellationToken);
}

public sealed record NotificationDeliveryResult(string Channel, bool Delivered, string? Detail = null)
{
    public static NotificationDeliveryResult Success(string channel) => new(channel, Delivered: true);

    public static NotificationDeliveryResult Failure(string channel, string detail) =>
        new(channel, Delivered: false, detail);
}
