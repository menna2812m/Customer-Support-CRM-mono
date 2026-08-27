using System.Net;
using System.Text.Json;
using Crm.IntegrationTests.Infrastructure;
using Shouldly;

namespace Crm.IntegrationTests.Auth;

/// <summary>
/// User Story 4, first half: the anonymous endpoints cannot be hammered (spec FR-036, FR-037).
///
/// Three endpoints in this feature must accept a caller who has not proved anything yet, which
/// makes them the only doors in the application that can be knocked on indefinitely. The limit is
/// what keeps a credential hunt from also being an outage.
/// </summary>
[Collection(DatabaseCollectionDefinition.Name)]
public sealed class RateLimitTests(SqlServerFixture database)
{
    [Fact]
    public async Task Exceeding_the_limit_is_refused_with_the_shared_contract_and_a_retry_after_header()
    {
        await using var harness = SignInHarness.Create(
            database.ConnectionString,
            new Dictionary<string, string?>
            {
                ["RateLimiting:Enabled"] = "true",
                ["RateLimiting:Policies:auth-sign-in:PermitLimit"] = "3",
                ["RateLimiting:Policies:auth-sign-in:WindowSeconds"] = "60",
            });

        using var client = harness.CreateClient();

        for (var attempt = 0; attempt < 3; attempt++)
        {
            var permitted = await client.GetAsync(SignIn);

            permitted.StatusCode.ShouldNotBe(HttpStatusCode.TooManyRequests);
        }

        var refused = await client.GetAsync(SignIn);

        refused.StatusCode.ShouldBe(HttpStatusCode.TooManyRequests);

        // Throttling answers with the same error contract as everything else - a client that can
        // read one failure can read this one (spec FR-016).
        using var document = JsonDocument.Parse(await refused.Content.ReadAsStringAsync());

        document.RootElement.GetProperty("code").GetString().ShouldBe("rate_limited");
        document.RootElement.GetProperty("correlationId").GetString().ShouldNotBeNullOrWhiteSpace();

        // Without this, a well-behaved client has no way to back off except by guessing.
        refused.Headers.RetryAfter.ShouldNotBeNull();
    }

    [Fact]
    public async Task One_abusive_source_does_not_consume_another_callers_allowance()
    {
        await using var harness = SignInHarness.Create(
            database.ConnectionString,
            new Dictionary<string, string?>
            {
                ["RateLimiting:Enabled"] = "true",
                ["RateLimiting:Policies:auth-sign-in:PermitLimit"] = "2",
                ["RateLimiting:Policies:auth-sign-in:WindowSeconds"] = "60",
            });

        using var client = harness.CreateClient();

        await SendFromAsync(client, "203.0.113.10");
        await SendFromAsync(client, "203.0.113.10");

        (await SendFromAsync(client, "203.0.113.10")).StatusCode.ShouldBe(HttpStatusCode.TooManyRequests);

        // A limit that is not partitioned is a denial of service with extra steps: one hostile
        // client would lock out every colleague behind the same address.
        (await SendFromAsync(client, "203.0.113.20")).StatusCode.ShouldNotBe(HttpStatusCode.TooManyRequests);
    }

    [Fact]
    public async Task The_two_policies_are_counted_separately()
    {
        await using var harness = SignInHarness.Create(
            database.ConnectionString,
            new Dictionary<string, string?>
            {
                ["RateLimiting:Enabled"] = "true",
                ["RateLimiting:Policies:auth-sign-in:PermitLimit"] = "2",
                ["RateLimiting:Policies:auth-sign-in:WindowSeconds"] = "60",
                ["RateLimiting:Policies:auth-session:PermitLimit"] = "20",
                ["RateLimiting:Policies:auth-session:WindowSeconds"] = "60",
            });

        using var client = harness.CreateClient();

        await client.GetAsync(SignIn);
        await client.GetAsync(SignIn);

        (await client.GetAsync(SignIn)).StatusCode.ShouldBe(HttpStatusCode.TooManyRequests);

        // Exhausting the sign-in allowance must not lock a signed-in user out of renewing. One
        // shared bucket would turn a burst of failed sign-ins into an outage for everybody working.
        using var renewal = await harness.RequestSessionAsync();

        renewal.StatusCode.ShouldNotBe(HttpStatusCode.TooManyRequests);
    }

    [Fact]
    public async Task Throttling_can_be_turned_off_where_it_would_only_obscure_what_is_under_test()
    {
        await using var harness = SignInHarness.Create(
            database.ConnectionString,
            new Dictionary<string, string?>
            {
                ["RateLimiting:Enabled"] = "false",
                ["RateLimiting:Policies:auth-sign-in:PermitLimit"] = "1",
            });

        using var client = harness.CreateClient();

        for (var attempt = 0; attempt < 5; attempt++)
        {
            (await client.GetAsync(SignIn)).StatusCode.ShouldNotBe(HttpStatusCode.TooManyRequests);
        }
    }

    private static async Task<HttpResponseMessage> SendFromAsync(HttpClient client, string source)
    {
        using var request = new HttpRequestMessage(HttpMethod.Get, SignIn);

        request.Headers.Add(TestClientAddressFilter.HeaderName, source);

        return await client.SendAsync(request);
    }

    private static Uri SignIn => new("/api/v1/auth/sign-in", UriKind.Relative);
}
