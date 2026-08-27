using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using Crm.IntegrationTests.Infrastructure;
using Shouldly;

namespace Crm.IntegrationTests.Auth;

/// <summary>
/// Ending a session must actually end it (spec FR-020, FR-021).
///
/// The credential the CRM issues is a signed token: nothing about the token itself changes when a
/// session is revoked. What makes revocation immediate is that every request checks the session is
/// still alive, and that check is what these tests exercise - a signed-out user holding a
/// still-unexpired token must be refused.
/// </summary>
[Collection(DatabaseCollectionDefinition.Name)]
public sealed class SignOutTests(SqlServerFixture database)
{
    [Fact]
    public async Task Signing_out_makes_a_credential_that_worked_a_moment_ago_stop_working()
    {
        var email = $"{Guid.CreateVersion7():n}@fake.local";

        await using var harness = SignInHarness.Create(
            database.ConnectionString,
            new Dictionary<string, string?> { ["Identity:BootstrapAdministrator"] = email });

        var account = harness.Provider.AddAccount(email: email);
        (await harness.SignInAsync(account)).Succeeded.ShouldBeTrue();

        var accessToken = await ReadAccessTokenAsync(harness);
        using var client = harness.CreateAuthenticatedClient(accessToken);

        (await client.GetAsync(new Uri("/api/v1/diagnostics/items", UriKind.Relative))).StatusCode
            .ShouldBe(HttpStatusCode.OK);

        var signedOut = await client.PostAsJsonAsync(
            new Uri("/api/v1/auth/sign-out", UriKind.Relative),
            new { allSessions = false, endProviderSession = false });

        signedOut.StatusCode.ShouldBe(HttpStatusCode.OK);

        // The same token, still within its lifetime, on the same client. Refused because the
        // session behind it is gone - which is the whole point of server-side sessions.
        (await client.GetAsync(new Uri("/api/v1/diagnostics/items", UriKind.Relative))).StatusCode
            .ShouldBe(HttpStatusCode.Unauthorized);
    }

    [Fact]
    public async Task Signing_out_ends_the_renewal_cookie_so_a_reload_does_not_restore_the_session()
    {
        var email = $"{Guid.CreateVersion7():n}@fake.local";

        await using var harness = SignInHarness.Create(
            database.ConnectionString,
            new Dictionary<string, string?> { ["Identity:BootstrapAdministrator"] = email });

        var account = harness.Provider.AddAccount(email: email);
        (await harness.SignInAsync(account)).Succeeded.ShouldBeTrue();

        var renewalBeforeSignOut = harness.RenewalCookie;
        renewalBeforeSignOut.ShouldNotBeNull();

        var accessToken = await ReadAccessTokenAsync(harness);
        using var client = harness.CreateAuthenticatedClient(accessToken);

        await client.PostAsJsonAsync(
            new Uri("/api/v1/auth/sign-out", UriKind.Relative),
            new { allSessions = false, endProviderSession = false });

        // Presenting the cookie the browser held before sign-out must not produce a new credential;
        // otherwise "sign out" would only last until the next page load.
        var renewed = await harness.RequestSessionAsync(renewalBeforeSignOut);

        renewed.StatusCode.ShouldBe(HttpStatusCode.Unauthorized);
    }

    [Fact]
    public async Task Ending_access_on_this_computer_is_offered_only_when_the_user_asks_for_it()
    {
        var email = $"{Guid.CreateVersion7():n}@fake.local";

        await using var harness = SignInHarness.Create(
            database.ConnectionString,
            new Dictionary<string, string?> { ["Identity:BootstrapAdministrator"] = email });

        var account = harness.Provider.AddAccount(email: email);
        (await harness.SignInAsync(account)).Succeeded.ShouldBeTrue();

        var accessToken = await ReadAccessTokenAsync(harness);
        using var client = harness.CreateAuthenticatedClient(accessToken);

        var response = await client.PostAsJsonAsync(
            new Uri("/api/v1/auth/sign-out", UriKind.Relative),
            new { allSessions = false, endProviderSession = true });

        using var document = JsonDocument.Parse(await response.Content.ReadAsStringAsync());

        // Signing out at the provider also ends every other corporate application in this browser,
        // so it is returned as an address the client may follow - never a redirect it must.
        document.RootElement.GetProperty("providerSignOutUrl").GetString()
            .ShouldNotBeNullOrWhiteSpace();
    }

    [Fact]
    public async Task Sign_out_refuses_a_request_that_does_not_carry_the_application_header()
    {
        var email = $"{Guid.CreateVersion7():n}@fake.local";

        await using var harness = SignInHarness.Create(
            database.ConnectionString,
            new Dictionary<string, string?> { ["Identity:BootstrapAdministrator"] = email });

        var account = harness.Provider.AddAccount(email: email);
        (await harness.SignInAsync(account)).Succeeded.ShouldBeTrue();

        var accessToken = await ReadAccessTokenAsync(harness);

        using var client = harness.CreateClient();
        using var request = new HttpRequestMessage(HttpMethod.Post, "/api/v1/auth/sign-out")
        {
            Content = JsonContent.Create(new { allSessions = false, endProviderSession = false }),
        };

        request.Headers.Authorization = new System.Net.Http.Headers.AuthenticationHeaderValue(
            "Bearer",
            accessToken);

        // No X-Requested-With. A cross-site form post cannot set one, which is what stops a hostile
        // page from signing a user out behind their back.
        (await client.SendAsync(request)).StatusCode.ShouldBe(HttpStatusCode.Forbidden);
    }

    private static async Task<string> ReadAccessTokenAsync(SignInHarness harness)
    {
        var session = await harness.RequestSessionAsync();
        session.StatusCode.ShouldBe(HttpStatusCode.OK);

        using var document = JsonDocument.Parse(await session.Content.ReadAsStringAsync());

        return document.RootElement.GetProperty("accessToken").GetString()!;
    }
}
