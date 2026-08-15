namespace ParcelNumberGenerator.ServiceDefaults;

/// <summary>
/// The names a service's own <c>Meter</c> and <c>ActivitySource</c> are registered under.
/// </summary>
/// <remarks>
/// Names only — no instruments. An instrument here would be a domain concept in the shared
/// kernel; each service declares its own against these names.
/// </remarks>
public static class ServiceTelemetry
{
    public const string MeterName = "ParcelNumberGenerator";

    public const string ActivitySourceName = "ParcelNumberGenerator";
}
