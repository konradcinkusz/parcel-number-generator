using System.Net;
using System.Net.Http.Json;
using Microsoft.Extensions.Logging;
using ParcelNumberGenerator.Contracts;

namespace ParcelNumberGenerator.Notifications.Services;

/// <summary>
/// Pushes notifications to the warehouse control system. Registered only when
/// <c>Notifications:Webhook:Endpoint</c> is configured (P8); absent it, the service runs
/// with <see cref="LoggingNotificationChannel"/> alone and nothing fails to start.
/// </summary>
public sealed partial class WebhookNotificationChannel(
    HttpClient httpClient,
    ILogger<WebhookNotificationChannel> logger) : INotificationChannel
{
    public const string HttpClientName = "notification-webhook";

    public string Name => "webhook";

    public async Task<NotificationDeliveryResult> DeliverAsync(
        NotificationResponse notification,
        CancellationToken cancellationToken)
    {
        try
        {
            using var response = await httpClient.PostAsJsonAsync(
                requestUri: (Uri?)null, notification, cancellationToken);

            // SERVICE-API-PATTERNS §5: a redirect between services is always a
            // configuration bug, and an http→https 301 silently turns this POST into a
            // GET. AllowAutoRedirect is off where the client is registered; a 3xx that
            // arrives anyway is reported, not followed.
            if (IsRedirect(response.StatusCode))
            {
                var location = response.Headers.Location?.ToString() ?? "(no Location header)";
                RedirectRejected(logger, httpClient.BaseAddress?.ToString() ?? "(unset)", location);

                return NotificationDeliveryResult.Failure(
                    Name, $"webhook responded {(int)response.StatusCode} redirecting to {location}");
            }

            if (!response.IsSuccessStatusCode)
            {
                DeliveryFailed(logger, (int)response.StatusCode);
                return NotificationDeliveryResult.Failure(Name, $"webhook responded {(int)response.StatusCode}");
            }

            return NotificationDeliveryResult.Success(Name);
        }
        catch (HttpRequestException exception)
        {
            DeliveryErrored(logger, exception);
            return NotificationDeliveryResult.Failure(Name, exception.Message);
        }
        catch (TaskCanceledException exception) when (!cancellationToken.IsCancellationRequested)
        {
            // The resilience handler's budget is spent. A slow control system must not
            // hold up the raise that has already been persisted.
            DeliveryTimedOut(logger, exception);
            return NotificationDeliveryResult.Failure(Name, "webhook timed out");
        }
    }

    private static bool IsRedirect(HttpStatusCode statusCode) =>
        (int)statusCode is >= 300 and < 400;

    [LoggerMessage(
        EventId = 2100,
        Level = LogLevel.Error,
        Message = "Webhook at {Configured} returned a redirect to {Location}; refusing to follow it")]
    private static partial void RedirectRejected(ILogger logger, string configured, string location);

    [LoggerMessage(EventId = 2101, Level = LogLevel.Warning, Message = "Webhook delivery failed with status {Status}")]
    private static partial void DeliveryFailed(ILogger logger, int status);

    [LoggerMessage(EventId = 2102, Level = LogLevel.Warning, Message = "Webhook delivery errored")]
    private static partial void DeliveryErrored(ILogger logger, Exception exception);

    [LoggerMessage(EventId = 2103, Level = LogLevel.Warning, Message = "Webhook delivery timed out")]
    private static partial void DeliveryTimedOut(ILogger logger, Exception exception);
}
