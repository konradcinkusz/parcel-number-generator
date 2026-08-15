using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;

namespace ParcelNumberGenerator.ServiceDefaults;

/// <summary>
/// Applies schema migrations once, in the background, after the server has started.
/// </summary>
/// <remarks>
/// <para>
/// A hosted service rather than a call in <c>Program.cs</c> before <c>Run()</c>, so health
/// probes answer while schema work is still in flight. A migration that takes longer than
/// the platform's grace period otherwise reads as a failed deploy and gets rolled back
/// mid-migration (P4).
/// </para>
/// <para>
/// <c>MigrateAsync</c> on every real provider; <c>EnsureCreatedAsync</c> only on the
/// in-memory provider, which has no migrations. The reverse — <c>EnsureCreated</c> against a
/// live database — is the mistake the reference architecture calls out by name: it creates
/// the schema at first boot and then silently ignores every later model change.
/// </para>
/// </remarks>
public sealed partial class DatabaseMigrationService<TContext>(
    IServiceScopeFactory scopeFactory,
    DatabaseProviderInfo providerInfo,
    ILogger<DatabaseMigrationService<TContext>> logger) : BackgroundService
    where TContext : DbContext
{
    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        using IServiceScope scope = scopeFactory.CreateScope();
        TContext db = scope.ServiceProvider.GetRequiredService<TContext>();
        string context = typeof(TContext).Name;

        if (!providerInfo.IsRelational)
        {
            await db.Database.EnsureCreatedAsync(stoppingToken).ConfigureAwait(false);
            LogInMemoryCreated(logger, context);
            return;
        }

        LogMigrationsStarting(logger, context);
        await db.Database.MigrateAsync(stoppingToken).ConfigureAwait(false);
        LogMigrationsApplied(logger, context);
    }

    [LoggerMessage(Level = LogLevel.Information, Message = "In-memory database created for {Context}.")]
    private static partial void LogInMemoryCreated(ILogger logger, string context);

    [LoggerMessage(Level = LogLevel.Information, Message = "Applying migrations for {Context}.")]
    private static partial void LogMigrationsStarting(ILogger logger, string context);

    [LoggerMessage(Level = LogLevel.Information, Message = "Migrations for {Context} applied.")]
    private static partial void LogMigrationsApplied(ILogger logger, string context);
}

public static class DatabaseMigrationExtensions
{
    public static IServiceCollection AddDatabaseMigration<TContext>(this IServiceCollection services)
        where TContext : DbContext =>
        services.AddHostedService<DatabaseMigrationService<TContext>>();
}
