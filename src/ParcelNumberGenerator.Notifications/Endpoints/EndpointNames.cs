namespace ParcelNumberGenerator.Notifications.Endpoints;

/// <summary>
/// Stable operation ids. Endpoints take their names from here rather than from string
/// literals, so a generated client's method names survive a route rename and this file
/// doubles as the contract another service compiles against
/// (SERVICE-API-PATTERNS §2).
/// </summary>
public static class EndpointNames
{
    public const string ListNotifications = "ListNotifications";
    public const string GetNotification = "GetNotification";
    public const string RaiseNotification = "RaiseNotification";
    public const string UpdateNotification = "UpdateNotification";
    public const string AcknowledgeNotification = "AcknowledgeNotification";
    public const string DeleteNotification = "DeleteNotification";
}
