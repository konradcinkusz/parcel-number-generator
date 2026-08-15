using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;

namespace ParcelNumberGenerator.ServiceDefaults;

public static class CorsPolicies
{
    public const string Frontend = "frontend";
}

/// <summary>
/// P2 — one named CORS policy per service, its origins read from configuration (P5) so
/// the same image serves every environment.
/// </summary>
public static class CorsExtensions
{
    public static IServiceCollection AddCorsPolicy(
        this IServiceCollection services,
        IConfiguration configuration,
        string policyName)
    {
        ArgumentNullException.ThrowIfNull(services);
        ArgumentNullException.ThrowIfNull(configuration);

        var origins = configuration.GetSection("Cors:AllowedOrigins").Get<string[]>() ?? [];

        services.AddCors(options => options.AddPolicy(policyName, policy =>
        {
            if (origins.Length is 0)
            {
                // No configured origins means no cross-origin caller is expected. Denying
                // is the safe default; AllowAnyOrigin here would be a permanent hole that
                // nobody notices because nothing breaks.
                policy.WithOrigins([]);
                return;
            }

            policy.WithOrigins(origins)
                .AllowAnyHeader()
                .AllowAnyMethod()
                .AllowCredentials();
        }));

        return services;
    }
}
