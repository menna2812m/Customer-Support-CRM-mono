using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Hosting;

namespace Crm.IntegrationTests.Infrastructure;

/// <summary>
/// Hosts the real API in memory against the run-scoped database, exercising the actual request
/// pipeline: correlation, error contract, versioning, authorization.
///
/// The environment name is configurable so tests can assert behaviour that differs outside
/// Development - the OpenAPI document must not be reachable there (AR-002), and transport
/// security must be enforced (FR-052).
/// </summary>
public sealed class CrmWebApplicationFactory(
    string connectionString,
    string environmentName = "Development",
    IDictionary<string, string?>? overrides = null) : WebApplicationFactory<Program>
{
    public const string TestOrigin = "https://localhost:4200";

    protected override void ConfigureWebHost(IWebHostBuilder builder)
    {
        ArgumentNullException.ThrowIfNull(builder);

        builder.UseEnvironment(environmentName);

        builder.ConfigureAppConfiguration(configuration =>
        {
            var settings = new Dictionary<string, string?>(StringComparer.OrdinalIgnoreCase)
            {
                ["Database:ConnectionString"] = connectionString,
                ["Database:AutoMigrateOnStartup"] = "false",
                ["Cors:AllowedOrigins:0"] = TestOrigin,
                ["Observability:LogFilePath"] = Path.Combine(Path.GetTempPath(), "crm-tests-.log"),
            };

            // Both schemes on, with local signing keys, so authorization is exercised for real.
            foreach (var (key, value) in TestTokens.AuthConfiguration())
            {
                settings[key] = value;
            }

            if (overrides is not null)
            {
                foreach (var (key, value) in overrides)
                {
                    settings[key] = value;
                }
            }

            configuration.AddInMemoryCollection(settings);
        });
    }
}
