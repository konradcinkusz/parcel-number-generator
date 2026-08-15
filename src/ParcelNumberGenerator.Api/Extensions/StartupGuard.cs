using ParcelNumberGenerator.Api.Configuration;
using ParcelNumberGenerator.ServiceDefaults;

namespace ParcelNumberGenerator.Api.Extensions;

/// <summary>
/// Refuses to start a production deployment that is only half configured.
/// </summary>
/// <remarks>
/// <para>
/// The fallbacks that make a credential-free clone runnable (P8) are the same fallbacks
/// that, reached in production, produce a service which looks healthy and is quietly wrong:
/// an in-memory database loses every issued number on restart and reissues them, and an
/// unauthenticated endpoint drains a finite pool for anyone who finds it. Both would pass a
/// health check.
/// </para>
/// <para>
/// So the fallbacks stay, and this decides where they are allowed. Failures name the
/// configuration key to set — an error that says what is missing costs nothing to write and
/// saves the reader a trip through the source.
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

        if (string.IsNullOrWhiteSpace(app.Configuration.GetConnectionString(ConnectionNames.ParcelNumbers)))
        {
            problems.Add(
                $"No connection string '{ConnectionNames.ParcelNumbers}'. Without it the service falls back " +
                $"to an in-memory database, which loses every issued number on restart and then reissues them. " +
                $"Set ConnectionStrings__{ConnectionNames.ParcelNumbers} and {DatabaseProviderExtensions.ProviderKey}.");
        }

        if (!app.Configuration.IsJwtConfigured() &&
            !app.Configuration.GetValue<bool>(AllowAnonymousKey))
        {
            problems.Add(
                $"No '{AuthenticationExtensions.AuthoritySection}:Authority'. Allocation permanently consumes a " +
                $"finite pool, so the endpoint is not left open by default. Set Jwt__Authority to the identity " +
                $"provider's URL, or set {AllowAnonymousKey.Replace(":", "__", StringComparison.Ordinal)}=true " +
                $"to accept the risk.");
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
