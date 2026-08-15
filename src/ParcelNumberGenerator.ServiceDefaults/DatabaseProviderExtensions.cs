using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;

namespace ParcelNumberGenerator.ServiceDefaults;

/// <summary>
/// Which database a service talks to is a configuration switch, not a compile-time decision
/// (P4).
/// </summary>
public static class DatabaseProviderExtensions
{
    /// <summary>Configuration key selecting the provider.</summary>
    public const string ProviderKey = "DATABASE_PROVIDER";

    public const string PostgreSql = "PostgreSQL";
    public const string SqlServer = "SqlServer";
    public const string InMemory = "InMemory";

    /// <summary>
    /// Overrides the in-memory database name. The in-memory provider keys its stores by
    /// name, so two hosts sharing one name share one database — which is what a test suite
    /// running several hosts in one process needs to be able to opt out of.
    /// </summary>
    public const string InMemoryNameKey = "Database:InMemoryName";

    /// <summary>
    /// Registers <typeparamref name="TContext"/> against the configured provider, falling
    /// back to the in-memory provider when no connection string is present.
    /// </summary>
    /// <remarks>
    /// The fallback is what makes <c>git clone &amp;&amp; dotnet run</c> work with no cloud
    /// credentials (P8) and lets the test suite run without a container.
    /// </remarks>
    public static IHostApplicationBuilder AddDatabaseContext<TContext>(
        this IHostApplicationBuilder builder,
        DatabaseContextSettings settings)
        where TContext : DbContext
    {
        ArgumentNullException.ThrowIfNull(builder);
        ArgumentNullException.ThrowIfNull(settings);

        string? connectionString = builder.Configuration.GetConnectionString(settings.ConnectionName);
        string provider = ResolveProvider(builder.Configuration, connectionString);

        builder.Services.AddDbContext<TContext>(options =>
        {
            switch (provider)
            {
                case PostgreSql:
                    options.UseNpgsql(Normalize(connectionString!), npgsql =>
                    {
                        npgsql.EnableRetryOnFailure(MaxRetryCount, MaxRetryDelay, errorCodesToAdd: null);
                        npgsql.CommandTimeout(CommandTimeoutSeconds);
                        npgsql.MigrationsAssembly(settings.PostgreSqlMigrationsAssembly);
                    });
                    break;

                case SqlServer:
                    options.UseSqlServer(Normalize(connectionString!), sqlServer =>
                    {
                        sqlServer.EnableRetryOnFailure(MaxRetryCount, MaxRetryDelay, errorNumbersToAdd: null);
                        sqlServer.CommandTimeout(CommandTimeoutSeconds);
                        sqlServer.MigrationsAssembly(settings.SqlServerMigrationsAssembly);
                    });
                    break;

                default:
                    options.UseInMemoryDatabase(
                        builder.Configuration[InMemoryNameKey] ?? settings.InMemoryDatabaseName);
                    break;
            }
        });

        builder.Services.AddSingleton(new DatabaseProviderInfo(provider));
        return builder;
    }

    /// <summary>
    /// Ten attempts over thirty seconds: sized for a scale-to-zero Postgres waking up, which
    /// is the failure this retry policy actually exists for.
    /// </summary>
    private const int MaxRetryCount = 10;

    private static readonly TimeSpan MaxRetryDelay = TimeSpan.FromSeconds(30);

    private const int CommandTimeoutSeconds = 60;

    private static string ResolveProvider(IConfiguration configuration, string? connectionString)
    {
        // No connection string means no database to talk to, whatever the provider key says.
        // Falling back here rather than failing is what keeps a credential-free clone
        // runnable; the API's own startup guard is what stops that fallback reaching
        // production silently.
        if (string.IsNullOrWhiteSpace(connectionString))
        {
            return InMemory;
        }

        return configuration[ProviderKey] switch
        {
            PostgreSql => PostgreSql,
            SqlServer => SqlServer,
            _ => PostgreSql,
        };
    }

    /// <summary>
    /// Rewrites a Fly.io <c>.flycast</c> host to <c>.internal</c>.
    /// </summary>
    /// <remarks>
    /// <c>.flycast</c> routes through Fly's proxy, which does not hold a connection open
    /// while a scaled-to-zero machine boots; <c>.internal</c> addresses the machine over 6PN
    /// directly. The symptom of getting this wrong is an intermittent connection reset on
    /// the first request after an idle period, which reads as a database fault rather than a
    /// routing one.
    /// </remarks>
    private static string Normalize(string connectionString) =>
        connectionString.Replace(".flycast", ".internal", StringComparison.OrdinalIgnoreCase);
}

/// <summary>
/// What a service tells the kernel about its own database.
/// </summary>
/// <remarks>
/// The migration assembly names are parameters rather than constants in here because a
/// migration set belongs to the service that owns the schema. Naming them in the kernel
/// would put one service's assembly on every other service's path — the first step of the
/// drift P2 exists to prevent.
/// </remarks>
/// <param name="ConnectionName">Key under <c>ConnectionStrings</c>.</param>
/// <param name="InMemoryDatabaseName">Name used when falling back to the in-memory provider.</param>
/// <param name="PostgreSqlMigrationsAssembly">Assembly holding the PostgreSQL migration set.</param>
/// <param name="SqlServerMigrationsAssembly">Assembly holding the SQL Server migration set.</param>
public sealed record DatabaseContextSettings(
    string ConnectionName,
    string InMemoryDatabaseName,
    string PostgreSqlMigrationsAssembly,
    string SqlServerMigrationsAssembly);

/// <summary>The provider a context was registered against. Reported by diagnostics.</summary>
public sealed record DatabaseProviderInfo(string Provider)
{
    public bool IsRelational => Provider is not DatabaseProviderExtensions.InMemory;
}
