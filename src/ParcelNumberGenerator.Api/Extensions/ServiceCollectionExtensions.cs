using Microsoft.AspNetCore.RateLimiting;
using Microsoft.Extensions.Options;
using ParcelNumberGenerator.Api.Configuration;
using ParcelNumberGenerator.Data;
using ParcelNumberGenerator.Domain;
using ParcelNumberGenerator.Domain.Allocation;
using ParcelNumberGenerator.ServiceDefaults;

namespace ParcelNumberGenerator.Api.Extensions;

/// <summary>
/// Everything <c>Program.cs</c> delegates to, so that file stays a list of capabilities
/// rather than configuration code (P9).
/// </summary>
public static class ServiceCollectionExtensions
{
    /// <summary>The strategies that exist. Used to validate configuration at startup.</summary>
    public static readonly string[] KnownStrategies =
    [
        AdaptiveAllocationStrategy.StrategyName,
        RandomProbeAllocationStrategy.StrategyName,
        SequentialScanAllocationStrategy.StrategyName,
    ];

    public const string AllocationRateLimitPolicy = "allocation";

    /// <summary>Options binding, validated on start rather than on first use.</summary>
    public static IHostApplicationBuilder AddParcelNumberOptions(this IHostApplicationBuilder builder)
    {
        builder.Services.AddOptions<PoolOptions>()
            .Bind(builder.Configuration.GetSection(PoolOptions.SectionName))
            .ValidateDataAnnotations()
            .Validate(
                options => !options.Validate().Any(),
                "Pool configuration is invalid.")
            .ValidateOnStart();

        builder.Services.AddOptions<AllocationOptions>()
            .Bind(builder.Configuration.GetSection(AllocationOptions.SectionName))
            .ValidateDataAnnotations()
            .Validate(
                options => KnownStrategies.Contains(options.Strategy, StringComparer.OrdinalIgnoreCase),
                $"Allocation:Strategy must be one of: {string.Join(", ", KnownStrategies)}.")
            .ValidateOnStart();

        return builder;
    }

    /// <summary>Persistence: the context, its schema migration, and the store over it.</summary>
    public static IHostApplicationBuilder AddParcelNumberPersistence(this IHostApplicationBuilder builder)
    {
        builder.AddDatabaseContext<ParcelNumbersDbContext>(new DatabaseContextSettings(
            ConnectionName: ConnectionNames.ParcelNumbers,
            InMemoryDatabaseName: "parcelnumbers",
            PostgreSqlMigrationsAssembly: "ParcelNumberGenerator.Migrations.PostgreSQL",
            SqlServerMigrationsAssembly: "ParcelNumberGenerator.Migrations.SqlServer"));

        builder.Services.AddDatabaseMigration<ParcelNumbersDbContext>();
        builder.Services.AddScoped<IUsedNumberStore, EfUsedNumberStore>();
        builder.Services.TryAddSingletonTimeProvider();

        return builder;
    }

    /// <summary>The pool, the strategies, and the service that coordinates them.</summary>
    public static IHostApplicationBuilder AddParcelNumberAllocation(this IHostApplicationBuilder builder)
    {
        builder.Services.AddSingleton<IRandomSource, SharedRandomSource>();

        // The pool is fixed for the lifetime of the process: normalizing exclusions is work
        // worth doing once, and every request then shares the same segment table.
        builder.Services.AddSingleton(provider =>
            provider.GetRequiredService<IOptions<PoolOptions>>().Value.ToPool());

        // The concrete strategies, so the adaptive one can compose them and each stays
        // selectable on its own.
        builder.Services.AddScoped(provider => new RandomProbeAllocationStrategy(
            provider.GetRequiredService<IUsedNumberStore>(),
            provider.GetRequiredService<IRandomSource>(),
            provider.MaxAttemptsOr(RandomProbeAllocationStrategy.DefaultMaxAttempts)));

        builder.Services.AddScoped(provider => new SequentialScanAllocationStrategy(
            provider.GetRequiredService<IUsedNumberStore>(),
            provider.GetRequiredService<IRandomSource>(),
            provider.MaxAttemptsOr(SequentialScanAllocationStrategy.DefaultMaxAttempts)));

        // One registration line per strategy — that is the whole cost of adding a fourth
        // (P10). Keyed, so configuration selects between them by name.
        builder.Services.AddKeyedScoped<IAllocationStrategy>(
            AdaptiveAllocationStrategy.StrategyName,
            (provider, _) => new AdaptiveAllocationStrategy(
                provider.GetRequiredService<RandomProbeAllocationStrategy>(),
                provider.GetRequiredService<SequentialScanAllocationStrategy>()));

        builder.Services.AddKeyedScoped<IAllocationStrategy>(
            RandomProbeAllocationStrategy.StrategyName,
            (provider, _) => provider.GetRequiredService<RandomProbeAllocationStrategy>());

        builder.Services.AddKeyedScoped<IAllocationStrategy>(
            SequentialScanAllocationStrategy.StrategyName,
            (provider, _) => provider.GetRequiredService<SequentialScanAllocationStrategy>());

        builder.Services.AddScoped(provider => provider.GetRequiredKeyedService<IAllocationStrategy>(
            provider.GetRequiredService<IOptions<AllocationOptions>>().Value.Strategy.ToLowerInvariant()));

        builder.Services.AddScoped<ParcelNumberService>();
        return builder;
    }

    /// <summary>
    /// A fixed window on the allocation endpoint.
    /// </summary>
    /// <remarks>
    /// Allocation is the one operation here that permanently consumes a finite resource, so
    /// an unthrottled caller in a loop does not merely load the service — it drains the pool
    /// and the numbers do not come back.
    /// </remarks>
    public static IHostApplicationBuilder AddParcelNumberRateLimiting(this IHostApplicationBuilder builder)
    {
        builder.Services.AddRateLimiter(options =>
        {
            options.RejectionStatusCode = StatusCodes.Status429TooManyRequests;
            options.AddFixedWindowLimiter(AllocationRateLimitPolicy, limiter =>
            {
                limiter.PermitLimit = 60;
                limiter.Window = TimeSpan.FromMinutes(1);
                limiter.QueueLimit = 0;
            });
        });

        return builder;
    }

    private static int MaxAttemptsOr(this IServiceProvider provider, int fallback)
    {
        int configured = provider.GetRequiredService<IOptions<AllocationOptions>>().Value.MaxAttempts;
        return configured > 0 ? configured : fallback;
    }

    private static IServiceCollection TryAddSingletonTimeProvider(this IServiceCollection services)
    {
        if (services.All(descriptor => descriptor.ServiceType != typeof(TimeProvider)))
        {
            services.AddSingleton(TimeProvider.System);
        }

        return services;
    }
}

/// <summary>
/// Connection string names. One constant per database, referenced by both the API and the
/// AppHost, so the dev composition and the service cannot disagree about the name.
/// </summary>
public static class ConnectionNames
{
    public const string ParcelNumbers = "parcelnumbersdb";
}
