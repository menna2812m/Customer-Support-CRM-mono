using System.Net.Http.Headers;
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

            // The application issues and validates its own credentials, so the tests configure the
            // issuer rather than a provider.
            foreach (var (key, value) in TestTokens.TokenConfiguration())
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

    /// <summary>
    /// Seeds a staff user with the given permissions, starts a real session, and returns a client
    /// carrying its credential. Tests get an authenticated caller without repeating the handshake,
    /// while still exercising the per-request session check.
    /// </summary>
    public async Task<HttpClient> SignInAsync(params string[] permissions)
    {
        var credential = await TestTokens.IssueStaffAsync(Services, permissions);
        var client = CreateClient();

        client.DefaultRequestHeaders.Authorization =
            new AuthenticationHeaderValue("Bearer", credential.AccessCredential);

        return client;
    }

    /// <summary>As <see cref="SignInAsync"/>, but also returns the seeded identifiers.</summary>
    public async Task<(HttpClient Client, IssuedTestCredential Credential)> SignInWithDetailsAsync(
        params string[] permissions)
    {
        var credential = await TestTokens.IssueStaffAsync(Services, permissions);
        var client = CreateClient();

        client.DefaultRequestHeaders.Authorization =
            new AuthenticationHeaderValue("Bearer", credential.AccessCredential);

        return (client, credential);
    }
}
