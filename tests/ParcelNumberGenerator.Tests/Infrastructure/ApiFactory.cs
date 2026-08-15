using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Hosting;
using ParcelNumberGenerator.ServiceDefaults;

namespace ParcelNumberGenerator.Tests.Infrastructure;

/// <summary>
/// Hosts the real application pipeline — the same <c>Program.cs</c>, the same DI graph —
/// over the in-memory provider.
/// </summary>
/// <remarks>
/// Each instance gets its own database name, so tests that drain a pool do not interfere
/// with each other and can run in parallel.
/// </remarks>
public sealed class ApiFactory : WebApplicationFactory<Program>
{
    private readonly Dictionary<string, string?> _settings;

    public ApiFactory(int from = 1, int to = 100, params (int From, int To)[] exclusions)
    {
        _settings = new Dictionary<string, string?>
        {
            ["Pool:From"] = from.ToString(System.Globalization.CultureInfo.InvariantCulture),
            ["Pool:To"] = to.ToString(System.Globalization.CultureInfo.InvariantCulture),
            [DatabaseProviderExtensions.InMemoryNameKey] = Guid.NewGuid().ToString(),
        };

        for (int i = 0; i < exclusions.Length; i++)
        {
            _settings[$"Pool:Exclusions:{i}:From"] = exclusions[i].From.ToString(System.Globalization.CultureInfo.InvariantCulture);
            _settings[$"Pool:Exclusions:{i}:To"] = exclusions[i].To.ToString(System.Globalization.CultureInfo.InvariantCulture);
        }
    }

    /// <summary>Overrides a configuration value for one test's host.</summary>
    public ApiFactory With(string key, string? value)
    {
        _settings[key] = value;
        return this;
    }

    protected override void ConfigureWebHost(IWebHostBuilder builder)
    {
        builder.UseEnvironment(Environments.Development);
        builder.ConfigureAppConfiguration((_, configuration) =>
        {
            // Appended last so it wins over appsettings.Development.json, which configures a
            // different pool.
            configuration.AddInMemoryCollection(_settings);
        });
    }
}
