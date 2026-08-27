using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using Crm.Application.Authorization;
using Crm.IntegrationTests.Infrastructure;
using Shouldly;

namespace Crm.IntegrationTests.Auth;

/// <summary>
/// User Story 3: permissions arrive with the session, and they are the CRM's own (spec FR-021,
/// FR-022, FR-025, FR-027).
///
/// The tests that matter here are the negative ones. A permission system is only as good as what
/// it refuses, and the two ways it typically fails are staleness - a role change that never lands -
/// and trust in something the caller supplied.
/// </summary>
[Collection(DatabaseCollectionDefinition.Name)]
public sealed class PermissionTests(SqlServerFixture database)
{
    [Fact]
    public async Task A_role_reaches_the_endpoints_it_permits_and_is_refused_by_the_others()
    {
        // The Agent role grants customer and ticket work; it deliberately does not grant the
        // diagnostics surface, which is the only permissioned endpoint the foundation ships.
        await using var harness = SignInHarness.Create(
            database.ConnectionString,
            new Dictionary<string, string?> { ["Identity:DefaultRole"] = "Agent" });

        var account = harness.Provider.AddAccount();
        (await harness.SignInAsync(account)).Succeeded.ShouldBeTrue();

        var credential = await harness.IssueAccessCredentialAsync();
        using var client = harness.CreateAuthenticatedClient(credential);

        var refused = await client.GetAsync(Diagnostics);

        refused.StatusCode.ShouldBe(HttpStatusCode.Forbidden);

        // Refused for the right reason: the caller is authenticated, so this is a permission
        // decision rather than a missing credential.
        (await ReadCodeAsync(refused)).ShouldBe("forbidden");

        // And the permissions the role does grant are present in the session, so the refusal above
        // is not simply an empty credential.
        var permissions = await ReadPermissionsAsync(harness);

        permissions.ShouldContain(Permissions.Tickets.View);
        permissions.ShouldContain(Permissions.Customers.View);
        permissions.ShouldNotContain(Permissions.Diagnostics.Read);
    }

    [Fact]
    public async Task A_role_change_lands_on_the_next_renewal_without_the_user_signing_out()
    {
        await using var harness = SignInHarness.Create(
            database.ConnectionString,
            new Dictionary<string, string?> { ["Identity:DefaultRole"] = "Agent" });

        var account = harness.Provider.AddAccount();
        (await harness.SignInAsync(account)).Succeeded.ShouldBeTrue();

        var userId = await harness.GetUserIdAsync(account.Subject);

        using (var before = harness.CreateAuthenticatedClient(await harness.IssueAccessCredentialAsync()))
        {
            (await before.GetAsync(Diagnostics)).StatusCode.ShouldBe(HttpStatusCode.Forbidden);
        }

        await harness.GrantRoleAsync(userId, "Administrator");

        // The credential already in the user's hands still says what it said when it was issued -
        // that is what a signed credential is. The bound on staleness is the renewal cycle, and
        // this is the assertion that the bound actually holds.
        using var after = harness.CreateAuthenticatedClient(await harness.IssueAccessCredentialAsync());

        (await after.GetAsync(Diagnostics)).StatusCode.ShouldBe(HttpStatusCode.OK);
    }

    [Fact]
    public async Task A_permission_claimed_in_the_request_payload_grants_nothing()
    {
        await using var harness = SignInHarness.Create(
            database.ConnectionString,
            new Dictionary<string, string?> { ["Identity:DefaultRole"] = "Agent" });

        var account = harness.Provider.AddAccount();
        (await harness.SignInAsync(account)).Succeeded.ShouldBeTrue();

        using var client = harness.CreateAuthenticatedClient(await harness.IssueAccessCredentialAsync());

        // Asking nicely. Permissions come from the signed credential and from nowhere else, so a
        // body, a header, or a query string asserting them changes nothing (spec FR-027).
        using var byQueryAndHeader = new HttpRequestMessage(
            HttpMethod.Get,
            $"/api/v1/diagnostics/items?permission={Permissions.Diagnostics.Read}");

        byQueryAndHeader.Headers.Add("X-Permissions", Permissions.Diagnostics.Read);
        byQueryAndHeader.Headers.Add("X-Crm-Population", "Staff");

        (await client.SendAsync(byQueryAndHeader)).StatusCode.ShouldBe(HttpStatusCode.Forbidden);

        using var byBody = new HttpRequestMessage(HttpMethod.Post, "/api/v1/diagnostics/echo")
        {
            Content = JsonContent.Create(new
            {
                message = "hello",
                permissions = new[] { Permissions.Diagnostics.Read },
            }),
        };

        (await client.SendAsync(byBody)).StatusCode.ShouldBe(HttpStatusCode.Forbidden);
    }

    [Fact]
    public async Task A_portal_credential_is_refused_by_a_staff_only_endpoint_even_holding_the_permission()
    {
        await using var harness = SignInHarness.Create(database.ConnectionString);

        var portal = await TestTokens.IssuePortalAsync(harness.Services, Permissions.Diagnostics.Read);

        using var client = harness.CreateAuthenticatedClient(portal.AccessCredential);

        // Holding the permission is not enough. The endpoint also declares who may reach it, and a
        // customer is not staff however their credential is decorated (spec AR-004).
        (await client.GetAsync(Diagnostics)).StatusCode.ShouldBe(HttpStatusCode.Forbidden);
    }

    [Fact]
    public async Task The_caller_can_read_their_own_identity_and_nothing_about_anybody_else()
    {
        var email = $"{Guid.CreateVersion7():n}@fake.local";

        await using var harness = SignInHarness.Create(
            database.ConnectionString,
            new Dictionary<string, string?> { ["Identity:BootstrapAdministrator"] = email });

        var account = harness.Provider.AddAccount(email: email, displayName: "Noura Al-Otaibi");
        (await harness.SignInAsync(account)).Succeeded.ShouldBeTrue();

        var userId = await harness.GetUserIdAsync(account.Subject);

        using var client = harness.CreateAuthenticatedClient(await harness.IssueAccessCredentialAsync());

        var response = await client.GetAsync(new Uri("/api/v1/auth/me", UriKind.Relative));

        response.StatusCode.ShouldBe(HttpStatusCode.OK);

        using var document = JsonDocument.Parse(await response.Content.ReadAsStringAsync());
        var body = document.RootElement;

        body.GetProperty("id").GetGuid().ShouldBe(userId);
        body.GetProperty("displayName").GetString().ShouldBe("Noura Al-Otaibi");
        body.GetProperty("population").GetString().ShouldBe("Staff");
        body.GetProperty("permissions").EnumerateArray().ShouldNotBeEmpty();
    }

    [Fact]
    public async Task Reading_your_own_identity_requires_a_credential()
    {
        await using var harness = SignInHarness.Create(database.ConnectionString);

        using var anonymous = harness.CreateClient();

        // Deny by default reaches this endpoint too: there is no anonymous "who am I".
        (await anonymous.GetAsync(new Uri("/api/v1/auth/me", UriKind.Relative))).StatusCode
            .ShouldBe(HttpStatusCode.Unauthorized);
    }

    private static async Task<List<string>> ReadPermissionsAsync(SignInHarness harness)
    {
        using var response = await harness.RequestSessionAsync();

        response.StatusCode.ShouldBe(HttpStatusCode.OK);

        using var document = JsonDocument.Parse(await response.Content.ReadAsStringAsync());

        return [.. document.RootElement
            .GetProperty("user")
            .GetProperty("permissions")
            .EnumerateArray()
            .Select(entry => entry.GetString()!)];
    }

    private static async Task<string?> ReadCodeAsync(HttpResponseMessage response)
    {
        using var document = JsonDocument.Parse(await response.Content.ReadAsStringAsync());

        return document.RootElement.TryGetProperty("code", out var code) ? code.GetString() : null;
    }

    private static Uri Diagnostics => new("/api/v1/diagnostics/items", UriKind.Relative);
}
