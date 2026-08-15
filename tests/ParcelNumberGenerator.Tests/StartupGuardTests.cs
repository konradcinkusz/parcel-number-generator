using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Hosting;
using ParcelNumberGenerator.Api.Extensions;
using ParcelNumberGenerator.ServiceDefaults;

namespace ParcelNumberGenerator.Tests;

/// <summary>
/// The fallbacks that make a credential-free clone runnable must not reach production
/// silently.
/// </summary>
public sealed class StartupGuardTests
{
    [Fact]
    public void Production_without_a_connection_string_refuses_to_start()
    {
        using ProductionFactory factory = new();

        InvalidOperationException exception =
            Assert.Throws<InvalidOperationException>(() => factory.CreateClient());

        // An in-memory database in production loses every issued number on restart and then
        // reissues them — a data-integrity failure that no health check would show.
        Assert.Contains("parcelnumbersdb", exception.Message, StringComparison.Ordinal);
    }

    [Fact]
    public void Production_without_an_issuer_refuses_to_start()
    {
        using ProductionFactory factory = new(new Dictionary<string, string?>
        {
            ["ConnectionStrings:parcelnumbersdb"] = "Host=db;Database=parcelnumbers",
            [DatabaseProviderExtensions.ProviderKey] = DatabaseProviderExtensions.PostgreSql,
        });

        InvalidOperationException exception =
            Assert.Throws<InvalidOperationException>(() => factory.CreateClient());

        Assert.Contains("Jwt", exception.Message, StringComparison.Ordinal);
    }

    [Fact]
    public void An_open_production_deployment_is_possible_but_has_to_be_asked_for()
    {
        using ProductionFactory factory = new(new Dictionary<string, string?>
        {
            ["ConnectionStrings:parcelnumbersdb"] = "Host=db;Database=parcelnumbers",
            [DatabaseProviderExtensions.ProviderKey] = DatabaseProviderExtensions.PostgreSql,
            [StartupGuard.AllowAnonymousKey] = "true",
        });

        // Still throws, but now only about the database: the connection string above points
        // at a host that does not exist, so this asserts the auth objection is gone rather
        // than that the service starts.
        InvalidOperationException? exception =
            Record.Exception(() => factory.CreateClient()) as InvalidOperationException;

        Assert.DoesNotContain("Jwt", exception?.Message ?? string.Empty, StringComparison.Ordinal);
    }

    [Fact]
    public void Development_starts_with_nothing_configured_at_all()
    {
        // P8's test: git clone && dotnet run, zero credentials, working system.
        using Infrastructure.ApiFactory factory = new();
        using HttpClient client = factory.CreateClient();

        Assert.NotNull(client);
    }

    private sealed class ProductionFactory(Dictionary<string, string?>? settings = null)
        : WebApplicationFactory<Program>
    {
        protected override void ConfigureWebHost(IWebHostBuilder builder)
        {
            builder.UseEnvironment(Environments.Production);
            builder.ConfigureAppConfiguration((_, configuration) =>
                configuration.AddInMemoryCollection(settings ?? []));
        }
    }
}
