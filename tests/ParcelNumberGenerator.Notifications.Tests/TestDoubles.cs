using ParcelNumberGenerator.Contracts;
using ParcelNumberGenerator.Notifications.Services;

namespace ParcelNumberGenerator.Notifications.Tests;

/// <summary>
/// A clock the test moves deliberately. The service takes <see cref="TimeProvider"/>
/// rather than reading <c>DateTimeOffset.UtcNow</c>, which is what makes
/// "acknowledging twice keeps the first timestamp" a test rather than a hope.
/// </summary>
internal sealed class FixedTimeProvider(DateTimeOffset now) : TimeProvider
{
    private DateTimeOffset _now = now;

    public override DateTimeOffset GetUtcNow() => _now;

    public void Advance(TimeSpan delta) => _now = _now.Add(delta);
}

internal sealed class RecordingChannel : INotificationChannel
{
    private readonly List<NotificationResponse> _delivered = [];

    public string Name => "recording";

    public IReadOnlyList<NotificationResponse> Delivered => _delivered;

    public Task<NotificationDeliveryResult> DeliverAsync(
        NotificationResponse notification,
        CancellationToken cancellationToken)
    {
        _delivered.Add(notification);
        return Task.FromResult(NotificationDeliveryResult.Success(Name));
    }
}

/// <summary>
/// A channel that is down. Stands in for the warehouse control system being unreachable,
/// which must not lose a notification that has already been persisted.
/// </summary>
internal sealed class FailingChannel : INotificationChannel
{
    public string Name => "failing";

    public Task<NotificationDeliveryResult> DeliverAsync(
        NotificationResponse notification,
        CancellationToken cancellationToken) =>
        Task.FromResult(NotificationDeliveryResult.Failure(Name, "control system unreachable"));
}
