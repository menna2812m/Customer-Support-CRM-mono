using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text.Json;
using Crm.Application.Authorization;
using Crm.IntegrationTests.Infrastructure;
using Microsoft.AspNetCore.HttpsPolicy;
using Microsoft.AspNetCore.TestHost;
using Microsoft.Extensions.DependencyInjection;
using Shouldly;

namespace Crm.IntegrationTests.Security;

/// <summary>
/// Edge hardening, enforced in the application so it survives a server rebuild and is verifiable
/// here (spec FR-052 to FR-055, SC-011).
/// </summary>
[Collection(DatabaseCollectionDefinition.Name)]
public sealed class HardeningTests(SqlServerFixture database)
{
    [Fact]
    public async Task Every_response_carries_the_baseline_security_headers()
    {
        await using var factory = new CrmWebApplicationFactory(database.ConnectionString);
        using var client = factory.CreateClient();

        var response = await client.GetAsync(new Uri("/health/live", UriKind.Relative));

        response.Headers.Contains("X-Content-Type-Options").ShouldBeTrue();
        response.Headers.GetValues("X-Content-Type-Options").ShouldContain("nosniff");
        response.Headers.GetValues("Content-Security-Policy").First().ShouldContain("frame-ancestors 'none'");
        response.Headers.GetValues("Referrer-Policy").ShouldContain("no-referrer");
        response.Headers.GetValues("X-Frame-Options").ShouldContain("DENY");
        response.Headers.Contains("Permissions-Policy").ShouldBeTrue();
    }

    [Fact]
    public async Task Transport_security_is_advertised_outside_development()
    {
        await using var factory = new CrmWebApplicationFactory(
            database.ConnectionString,
            environmentName: "Production");

        // HSTS deliberately skips localhost in production defaults, and the test host is localhost.
        // Clearing the exclusion is what lets the real middleware be observed here.
        using var client = factory
            .WithWebHostBuilder(builder => builder.ConfigureTestServices(services =>
                services.Configure<HstsOptions>(options => options.ExcludedHosts.Clear())))
            .CreateClient(new Microsoft.AspNetCore.Mvc.Testing.WebApplicationFactoryClientOptions
            {
                BaseAddress = new Uri("https://localhost"),
            });

        var response = await client.GetAsync(new Uri("/health/live", UriKind.Relative));

        response.Headers.Contains("Strict-Transport-Security").ShouldBeTrue();
    }

    [Fact]
    public async Task An_insecure_request_is_redirected_outside_development()
    {
        await using var factory = new CrmWebApplicationFactory(
            database.ConnectionString,
            environmentName: "Production");

        // The test host has no bound HTTPS port to infer, so it is supplied explicitly.
        using var client = factory
            .WithWebHostBuilder(builder => builder.ConfigureTestServices(services =>
                services.Configure<HttpsRedirectionOptions>(options => options.HttpsPort = 443)))
            .CreateClient(new Microsoft.AspNetCore.Mvc.Testing.WebApplicationFactoryClientOptions
            {
                AllowAutoRedirect = false,
                BaseAddress = new Uri("http://localhost"),
            });

        var response = await client.GetAsync(new Uri("/health/live", UriKind.Relative));

        ((int)response.StatusCode).ShouldBeInRange(300, 399);
        response.Headers.Location?.Scheme.ShouldBe("https");
    }

    [Fact]
    public async Task An_origin_outside_the_allowlist_receives_no_cors_headers()
    {
        await using var factory = new CrmWebApplicationFactory(database.ConnectionString);
        using var client = factory.CreateClient();

        using var allowed = new HttpRequestMessage(HttpMethod.Get, "/health/live");
        allowed.Headers.Add("Origin", CrmWebApplicationFactory.TestOrigin);

        using var refused = new HttpRequestMessage(HttpMethod.Get, "/health/live");
        refused.Headers.Add("Origin", "https://not-our-frontend.example");

        var allowedResponse = await client.SendAsync(allowed);
        var refusedResponse = await client.SendAsync(refused);

        allowedResponse.Headers.Contains("Access-Control-Allow-Origin").ShouldBeTrue();

        // No header means the browser blocks the read - which is exactly the intent.
        refusedResponse.Headers.Contains("Access-Control-Allow-Origin").ShouldBeFalse();
    }

    [Fact]
    public async Task A_wildcard_origin_stops_startup()
    {
        await using var factory = new CrmWebApplicationFactory(
            database.ConnectionString,
            environmentName: "Production",
            overrides: new Dictionary<string, string?> { ["Cors:AllowedOrigins:0"] = "*" });

        // Failing to start is the point: a wildcard would quietly undo the allowlist.
        var failure = Should.Throw<Exception>(() => factory.CreateClient());

        failure.ToString().ShouldContain("AllowedOrigins");
    }

    [Fact]
    public async Task An_over_length_collection_is_rejected_with_the_error_contract()
    {
        await using var factory = new CrmWebApplicationFactory(database.ConnectionString);
        using var client = await factory.SignInAsync(Permissions.Diagnostics.Read);

        var response = await client.PostAsJsonAsync(
            new Uri("/api/v1/diagnostics/echo", UriKind.Relative),
            new
            {
                message = "ok",
                repeatCount = 1,
                tags = Enumerable.Range(0, 501).Select(index => $"tag-{index}").ToArray(),
            });

        response.StatusCode.ShouldBe(HttpStatusCode.BadRequest);

        using var document = JsonDocument.Parse(await response.Content.ReadAsStringAsync());
        document.RootElement.GetProperty("code").GetString().ShouldBe("validation_failed");
        document.RootElement
            .GetProperty("errors")[0]
            .GetProperty("code")
            .GetString()
            .ShouldBe("too_many_items");
    }

    [Fact]
    public async Task An_over_deep_payload_is_rejected_with_the_error_contract()
    {
        await using var factory = new CrmWebApplicationFactory(database.ConnectionString);
        using var client = await factory.SignInAsync(Permissions.Diagnostics.Read);

        // 40 levels of nesting, comfortably past the configured depth of 32.
        var payload = string.Concat(Enumerable.Repeat("{\"a\":", 40))
            + "1"
            + string.Concat(Enumerable.Repeat("}", 40));

        using var content = new StringContent(payload, System.Text.Encoding.UTF8, "application/json");
        var response = await client.PostAsync(new Uri("/api/v1/diagnostics/echo", UriKind.Relative), content);

        response.StatusCode.ShouldBe(HttpStatusCode.BadRequest);

        var body = await response.Content.ReadAsStringAsync();
        body.ShouldContain("malformed_request");

        // A framework stack trace would leak the parser internals; the contract must hold here too.
        body.ShouldNotContain("System.Text.Json");
    }
}
