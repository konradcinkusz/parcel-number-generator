using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Diagnostics.HealthChecks;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Diagnostics.HealthChecks;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using OpenTelemetry;
using OpenTelemetry.Logs;
using OpenTelemetry.Metrics;
using OpenTelemetry.Trace;

namespace ParcelNumberGenerator.ServiceDefaults;

/// <summary>
/// The shared kernel: cross-cutting plumbing every service in this system opts into, one
/// line at a time.
/// </summary>
/// <remarks>
/// <para>
/// Extension methods only — no base class, nothing to derive from (P2). What must never
/// appear in this project: an entity, a DTO, a pool definition, a pricing constant, a seed
/// dataset or a user-facing string. Those belong to the service that owns them. The estate
/// has twice watched a shared kernel grow into a shared domain, so the boundary is asserted
/// by a test (<c>SharedKernelTests</c>) and a size check in CI, not by this paragraph.
/// </para>
/// </remarks>
public static class Extensions
{
    /// <summary>Telemetry, health, service discovery and HTTP resilience.</summary>
    public static TBuilder AddServiceDefaults<TBuilder>(this TBuilder builder)
        where TBuilder : IHostApplicationBuilder
    {
        builder.ConfigureOpenTelemetry();
        builder.AddDefaultHealthChecks();

        builder.Services.AddServiceDiscovery();
        builder.Services.ConfigureHttpClientDefaults(http =>
        {
            // Every outbound client gets retries, a circuit breaker and explicit timeouts by
            // default. Opting out has to be deliberate; forgetting is not an option.
            http.AddStandardResilienceHandler();
            http.AddServiceDiscovery();
        });

        return builder;
    }

    public static TBuilder ConfigureOpenTelemetry<TBuilder>(this TBuilder builder)
        where TBuilder : IHostApplicationBuilder
    {
        builder.Logging.AddOpenTelemetry(logging =>
        {
            logging.IncludeFormattedMessage = true;
            logging.IncludeScopes = true;
        });

        builder.Services.AddOpenTelemetry()
            .WithMetrics(metrics => metrics
                .AddAspNetCoreInstrumentation()
                .AddHttpClientInstrumentation()
                .AddRuntimeInstrumentation()
                .AddMeter(ServiceTelemetry.MeterName))
            .WithTracing(tracing => tracing
                .AddSource(ServiceTelemetry.ActivitySourceName)
                .AddAspNetCoreInstrumentation(tracing =>
                    // Probes fire every few seconds and carry no information. Left in, they
                    // are the majority of every trace export bill.
                    tracing.Filter = context => !IsHealthProbe(context.Request.Path))
                .AddHttpClientInstrumentation());

        builder.AddOpenTelemetryExporters();
        return builder;
    }

    private static TBuilder AddOpenTelemetryExporters<TBuilder>(this TBuilder builder)
        where TBuilder : IHostApplicationBuilder
    {
        bool useOtlpExporter = !string.IsNullOrWhiteSpace(
            builder.Configuration["OTEL_EXPORTER_OTLP_ENDPOINT"]);

        if (useOtlpExporter)
        {
            builder.Services.AddOpenTelemetry().UseOtlpExporter();
        }

        return builder;
    }

    public static TBuilder AddDefaultHealthChecks<TBuilder>(this TBuilder builder)
        where TBuilder : IHostApplicationBuilder
    {
        builder.Services.AddHealthChecks()
            .AddCheck("self", () => HealthCheckResult.Healthy(), ["live"]);

        return builder;
    }

    /// <summary>
    /// Maps <c>/health</c> (readiness — every check) and <c>/alive</c> (liveness — the
    /// <c>live</c>-tagged checks only).
    /// </summary>
    /// <remarks>
    /// The split is what stops an orchestrator restarting a process that is running fine but
    /// waiting on a dependency: a failing readiness probe should take a service out of
    /// rotation, and only a failing liveness probe should kill it.
    /// </remarks>
    public static WebApplication MapDefaultEndpoints(this WebApplication app)
    {
        app.MapHealthChecks(HealthPath);
        app.MapHealthChecks(AlivePath, new HealthCheckOptions
        {
            Predicate = registration => registration.Tags.Contains("live"),
        });

        return app;
    }

    public const string HealthPath = "/health";
    public const string AlivePath = "/alive";

    private static bool IsHealthProbe(PathString path) =>
        path.StartsWithSegments(HealthPath) || path.StartsWithSegments(AlivePath);
}
