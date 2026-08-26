using System.Net;
using System.Text.Json;
using Crm.IntegrationTests.Infrastructure;
using Shouldly;

namespace Crm.IntegrationTests.Health;

/// <summary>
/// Spec US1 acceptance: health reflects reality, is anonymous, and leaks nothing (FR-044, AR-001).
/// </summary>
[Collection(DatabaseCollectionDefinition.Name)]
public sealed class HealthEndpointTests(SqlServerFixture database)
{
    [Fact]
    public async Task Liveness_reports_healthy_without_credentials()
    {
        await using var factory = new CrmWebApplicationFactory(database.ConnectionString);
        using var client = factory.CreateClient();

        var response = await client.GetAsync(new Uri("/health/live", UriKind.Relative));

        response.StatusCode.ShouldBe(HttpStatusCode.OK);
        var payload = await response.Content.ReadAsStringAsync();
        ReadStatus(payload).ShouldBe("Healthy");
    }

    [Fact]
    public async Task Readiness_reports_healthy_and_lists_the_database_check()
    {
        await using var factory = new CrmWebApplicationFactory(database.ConnectionString);
        using var client = factory.CreateClient();

        var response = await client.GetAsync(new Uri("/health/ready", UriKind.Relative));

        response.StatusCode.ShouldBe(HttpStatusCode.OK);

        var payload = await response.Content.ReadAsStringAsync();
        ReadStatus(payload).ShouldBe("Healthy");
        payload.ShouldContain("database");
    }

    [Fact]
    public async Task Readiness_reports_unhealthy_without_exposing_connection_details()
    {
        // Point the host at a server that cannot be reached, which is what an outage looks like.
        const string unreachable =
            "Server=127.0.0.1,14330;Database=CrmMissing;User Id=sa;Password=NotTheRealPassword1!;"
            + "TrustServerCertificate=True;Connect Timeout=2";

        await using var factory = new CrmWebApplicationFactory(unreachable);
        using var client = factory.CreateClient();

        var response = await client.GetAsync(new Uri("/health/ready", UriKind.Relative));

        response.StatusCode.ShouldBe(HttpStatusCode.ServiceUnavailable);

        var payload = await response.Content.ReadAsStringAsync();
        ReadStatus(payload).ShouldBe("Unhealthy");

        // Anonymous endpoint: no server name, credential, or exception text may appear.
        payload.ShouldNotContain("127.0.0.1");
        payload.ShouldNotContain("NotTheRealPassword1!");
        payload.ShouldNotContain("Password");
        payload.ShouldNotContain("Exception");
    }

    private static string ReadStatus(string payload) =>
        JsonDocument.Parse(payload).RootElement.GetProperty("status").GetString() ?? string.Empty;
}
