using System.Globalization;
using System.Security.Claims;
using System.Threading.RateLimiting;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.RateLimiting;
using Microsoft.Extensions.DependencyInjection;

namespace ParcelNumberGenerator.ServiceDefaults;

/// <summary>
/// The rate-limit policy names, shared so an endpoint tags itself with a constant rather
/// than a string literal that can drift from the registration.
/// </summary>
public static class RateLimitPolicies
{
    /// <summary>Strict: for the brute-force surface (login, token exchange).</summary>
    public const string Sensitive = "sensitive";

    /// <summary>Generous per-user window: normal authenticated endpoints.</summary>
    public const string Api = "api";
}

/// <summary>
/// SERVICE-API-PATTERNS §1. Rate limiting is plumbing, so it lives in the kernel — four
/// hand-copied variants is how the policies drift apart.
/// </summary>
public static class RateLimitingExtensions
{
    public static IServiceCollection AddStandardRateLimiting(this IServiceCollection services)
    {
        ArgumentNullException.ThrowIfNull(services);

        services.AddRateLimiter(options =>
        {
            options.AddPolicy(RateLimitPolicies.Sensitive, context =>
                RateLimitPartition.GetFixedWindowLimiter(PartitionKey(context), _ => new FixedWindowRateLimiterOptions
                {
                    PermitLimit = 10,
                    Window = TimeSpan.FromMinutes(1),
                    QueueLimit = 0,
                }));

            options.AddPolicy(RateLimitPolicies.Api, context =>
                RateLimitPartition.GetFixedWindowLimiter(PartitionKey(context), _ => new FixedWindowRateLimiterOptions
                {
                    PermitLimit = 300,
                    Window = TimeSpan.FromMinutes(1),
                    QueueLimit = 0,
                }));

            // The catch-all, so an endpoint nobody remembered to tag is still bounded.
            options.GlobalLimiter = PartitionedRateLimiter.Create<HttpContext, string>(context =>
                RateLimitPartition.GetFixedWindowLimiter(PartitionKey(context), _ => new FixedWindowRateLimiterOptions
                {
                    PermitLimit = 600,
                    Window = TimeSpan.FromMinutes(1),
                    QueueLimit = 0,
                }));

            options.OnRejected = async (context, cancellationToken) =>
            {
                context.HttpContext.Response.StatusCode = StatusCodes.Status429TooManyRequests;

                var retryAfterSeconds = context.Lease.TryGetMetadata(MetadataName.RetryAfter, out var retryAfter)
                    ? (int)retryAfter.TotalSeconds
                    : 60;

                context.HttpContext.Response.Headers.RetryAfter =
                    retryAfterSeconds.ToString(CultureInfo.InvariantCulture);

                // One rejection shape for the whole estate, so every client learns it once.
                await context.HttpContext.Response.WriteAsJsonAsync(
                    new { error = "rate_limit_exceeded", retryAfter = retryAfterSeconds },
                    cancellationToken);
            };
        });

        return services;
    }

    /// <summary>
    /// Partition by authenticated user id, falling back to client IP. The partition key is
    /// the point: a non-partitioned window is one shared bucket for the whole deployment,
    /// and the first team behind a corporate NAT gets collectively 429'd.
    /// </summary>
    private static string PartitionKey(HttpContext context)
    {
        var userId = context.User.FindFirstValue(ClaimTypes.NameIdentifier)
            ?? context.User.FindFirstValue("sub");

        if (!string.IsNullOrWhiteSpace(userId))
        {
            return $"user:{userId}";
        }

        return $"ip:{context.Connection.RemoteIpAddress?.ToString() ?? "unknown"}";
    }
}
