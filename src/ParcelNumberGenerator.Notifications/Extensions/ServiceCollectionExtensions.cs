using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using ParcelNumberGenerator.Notifications.Data;
using ParcelNumberGenerator.Notifications.Services;
using ParcelNumberGenerator.ServiceDefaults;

namespace ParcelNumberGenerator.Notifications.Extensions;

/// <summary>
/// P9 — the wiring <c>Program.cs</c> delegates to, so the composition root stays a list
/// of capabilities rather than a page of configuration code.
/// </summary>
public static class ServiceCollectionExtensions
{
    public const string ConnectionStringName = "notificationsdb";

    public static IHostApplicationBuilder AddNotificationPersistence(this IHostApplicationBuilder builder)
    {
        builder.AddDatabaseContext<NotificationsDbContext>(new DatabaseContextSettings(
            ConnectionName: ConnectionStringName,
            InMemoryDatabaseName: "notifications",
            PostgreSqlMigrationsAssembly: "ParcelNumberGenerator.Notifications.Migrations.PostgreSQL",
            SqlServerMigrationsAssembly: "ParcelNumberGenerator.Notifications.Migrations.SqlServer"));

        builder.Services.AddDatabaseMigration<NotificationsDbContext>();

        return builder;
    }

    public static IServiceCollection AddNotificationServices(
        this IServiceCollection services,
        IConfiguration configuration)
    {
        ArgumentNullException.ThrowIfNull(configuration);

        services.AddSingleton(TimeProvider.System);
        services.AddScoped<NotificationService>();

        // P8 — the fallback channel is always registered, so a raise always has somewhere
        // to go.
        services.AddScoped<INotificationChannel, LoggingNotificationChannel>();

        var webhookEndpoint = configuration["Notifications:Webhook:Endpoint"];

        if (!string.IsNullOrWhiteSpace(webhookEndpoint))
        {
            services.AddHttpClient<WebhookNotificationChannel>(
                    WebhookNotificationChannel.HttpClientName,
                    client =>
                    {
                        client.BaseAddress = new Uri(webhookEndpoint);

                        // SERVICE-API-PATTERNS §5: advisory call, short timeout. This caps
                        // the resilience handler's total budget rather than extending it,
                        // which is why the handler's own timeouts are set below it.
                        client.Timeout = TimeSpan.FromSeconds(10);
                    })
                .ConfigurePrimaryHttpMessageHandler(() => new HttpClientHandler
                {
                    // An http→https 301 silently converts POST to GET; a create request
                    // then "succeeds" against the wrong verb. Detect, never follow.
                    AllowAutoRedirect = false,
                })
                .AddStandardResilienceHandler(options =>
                {
                    options.AttemptTimeout.Timeout = TimeSpan.FromSeconds(3);
                    options.TotalRequestTimeout.Timeout = TimeSpan.FromSeconds(9);
                    options.CircuitBreaker.SamplingDuration = TimeSpan.FromSeconds(6);
                });

            services.AddScoped<INotificationChannel>(provider =>
                provider.GetRequiredService<WebhookNotificationChannel>());
        }

        return services;
    }
}
