using Microsoft.AspNetCore.Builder;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Hosting;
using ParcelNumberGenerator.ServiceDefaults;

namespace ParcelNumberGenerator.Notifications.Extensions;

/// <summary>
/// Refuses to start a production deployment that is only half configured.
/// </summary>
/// <remarks>
/// <para>
/// The fallbacks that make a credential-free clone runnable (P8) are the same fallbacks
/// that, reached in production, produce a service which looks healthy and is quietly wrong:
/// an in-memory database loses every notification on restart, and an unauthenticated
/// endpoint serves operational detail about customers' shipments to anyone who finds it.
/// Both would pass a health check.
/// </para>
/// <para>
/// So the fallbacks stay, and this decides where they are allowed. This is the same guard
/// posture as the generator API — one rule for the estate — and it replaces the transferred
/// service's earlier throw-at-startup-without-an-issuer behaviour, recorded in ADR-0004.
/// </para>
/// </remarks>
public static class StartupGuard
{
    /// <summary>
    /// Set to <c>true</c> to run a production deployment with no authentication. Deliberately
    /// verbose: switching it on should look like a decision in a diff.
    /// </summary>
    public const string AllowAnonymousKey = "Security:AllowAnonymousAccess";

    public static WebApplication EnsureProductionIsConfigured(this WebApplication app)
    {
        if (!app.Environment.IsProduction())
        {
            return app;
        }

        List<string> problems = [];

        if (string.IsNullOrWhiteSpace(app.Configuration.GetConnectionString(ServiceCollectionExtensions.ConnectionStringName)))
        {
            problems.Add(
                $"No connection string '{ServiceCollectionExtensions.ConnectionStringName}'. Without it the service " +
                $"falls back to an in-memory database, which loses every notification on restart. " +
                $"Set ConnectionStrings__{ServiceCollectionExtensions.ConnectionStringName} and {DatabaseProviderExtensions.ProviderKey}.");
        }

        if (!app.Configuration.IsJwtConfigured() &&
            !app.Configuration.GetValue<bool>(AllowAnonymousKey))
        {
            problems.Add(
                $"No '{AuthenticationExtensions.AuthoritySection}:Authority'. A parcel number plus an exception " +
                $"message is operational detail about a customer's shipment, so the endpoints are not left open " +
                $"by default. Set Jwt__Authority to the identity provider's URL, or set " +
                $"{AllowAnonymousKey.Replace(":", "__", StringComparison.Ordinal)}=true to accept the risk.");
        }

        if (problems.Count > 0)
        {
            throw new InvalidOperationException(
                "Refusing to start in Production:" + Environment.NewLine +
                string.Join(Environment.NewLine, problems.Select(problem => "  - " + problem)));
        }

        return app;
    }
}
