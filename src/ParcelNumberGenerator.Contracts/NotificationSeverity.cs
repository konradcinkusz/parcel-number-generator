namespace ParcelNumberGenerator.Contracts;

/// <summary>
/// How loudly a notification should present. Carried over from the legacy
/// <c>MessageType</c> enum, whose four members were bound one-to-one to the icon bitmaps
/// the WinForms client shipped; the names now describe severity rather than which PNG
/// file to draw, so a non-graphical client (a handheld terminal, a webhook consumer) can
/// act on it.
/// </summary>
public enum NotificationSeverity
{
    /// <summary>No severity was supplied. The legacy <c>MessageType.None</c>.</summary>
    Unspecified = 0,

    Information = 1,

    Warning = 2,

    Error = 3,
}

/// <summary>
/// The warehouse event that caused a notification to be raised. <see cref="Manual"/>
/// covers a notification a human typed, which is every notification the legacy system
/// could produce.
/// </summary>
public enum ParcelEventKind
{
    Manual = 0,

    /// <summary>Parcel booked in at goods-in.</summary>
    Received = 1,

    /// <summary>Parcel moved to a storage location.</summary>
    PutAway = 2,

    Picked = 3,

    Packed = 4,

    Dispatched = 5,

    /// <summary>Damage, mis-scan, address failure — anything needing a human.</summary>
    Exception = 6,
}
