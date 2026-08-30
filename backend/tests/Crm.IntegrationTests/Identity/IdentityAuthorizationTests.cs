using System.Net;
using System.Net.Http.Json;
using Crm.IntegrationTests.Infrastructure;
using Shouldly;
using static Crm.IntegrationTests.Organization.OrganizationHarness;

namespace Crm.IntegrationTests.Identity;

/// <summary>
/// Spec AR-001 to AR-003, Constitution IV: the backend refuses, whatever the interface offers.
///
/// This is the most valuable file in the feature. Hiding a control is a courtesy; these tests are
/// the boundary, and a person who types the address directly meets exactly the same refusal.
/// </summary>
[Collection(DatabaseCollectionDefinition.Name)]
public sealed class IdentityAuthorizationTests(SqlServerFixture database)
{
    private const string People = "/api/v1/identity/people";
    private const string Roles = "/api/v1/identity/roles";

    [Fact]
    public async Task An_anonymous_caller_is_refused_before_anything_else()
    {
        await using var harness = SignInHarness.Create(database.ConnectionString);
        using var anonymous = harness.CreateClient();

        var response = await anonymous.GetAsync(Route(People));

        response.StatusCode.ShouldBe(HttpStatusCode.Unauthorized);
        (await ReadCodeAsync(response)).ShouldBe("unauthenticated");
    }

    [Fact]
    public async Task An_agent_can_neither_read_nor_change_people()
    {
        // The Agent role deliberately holds neither identity permission. Administering people is
        // not day-to-day work, and an agent has no reason to browse the staff list either.
        await using var harness = SignInHarness.Create(
            database.ConnectionString,
            new Dictionary<string, string?> { ["Identity:DefaultRole"] = "Agent" });

        var account = harness.Provider.AddAccount();
        (await harness.SignInAsync(account)).Succeeded.ShouldBeTrue();

        using var client = harness.CreateAuthenticatedClient(await harness.IssueAccessCredentialAsync());

        var read = await client.GetAsync(Route(People));
        var write = await client.PostAsJsonAsync(
            Route(People),
            new { email = $"{Tag()}@refused.local", displayName = "Attempt" });

        read.StatusCode.ShouldBe(HttpStatusCode.Forbidden);
        write.StatusCode.ShouldBe(HttpStatusCode.Forbidden);

        // Refused for the right reason: authenticated, but not permitted.
        (await ReadCodeAsync(read)).ShouldBe("forbidden");
    }

    [Fact]
    public async Task The_roles_list_is_refused_to_a_caller_without_the_view_permission()
    {
        await using var harness = SignInHarness.Create(
            database.ConnectionString,
            new Dictionary<string, string?> { ["Identity:DefaultRole"] = "Agent" });

        var account = harness.Provider.AddAccount();
        await harness.SignInAsync(account);

        using var client = harness.CreateAuthenticatedClient(await harness.IssueAccessCredentialAsync());

        (await client.GetAsync(Route(Roles))).StatusCode.ShouldBe(HttpStatusCode.Forbidden);
    }

    /// <summary>
    /// Every write refuses a caller who lacks <c>identity.manage</c>, not merely the ones an
    /// interface happens to hide. Enumerated so that adding an endpoint without its permission
    /// fails here rather than in production.
    /// </summary>
    [Theory]
    [InlineData("POST", "")]
    [InlineData("PUT", "/{0}/placement")]
    [InlineData("PUT", "/{0}/activation")]
    [InlineData("DELETE", "/{0}")]
    public async Task Every_write_is_refused_without_the_manage_permission(string method, string suffix)
    {
        await using var harness = SignInHarness.Create(
            database.ConnectionString,
            new Dictionary<string, string?> { ["Identity:DefaultRole"] = "Agent" });

        var account = harness.Provider.AddAccount();
        await harness.SignInAsync(account);

        using var client = harness.CreateAuthenticatedClient(await harness.IssueAccessCredentialAsync());

        var path = People + string.Format(System.Globalization.CultureInfo.InvariantCulture, suffix, Guid.NewGuid());

        using var request = new HttpRequestMessage(new HttpMethod(method), Route(path))
        {
            Content = JsonContent.Create(new { email = "x@y.local", displayName = "x", isActive = false }),
        };

        var response = await client.SendAsync(request);

        // Forbidden rather than NotFound: authorization is decided before the record is looked up,
        // so a caller who may not administer people cannot use these endpoints to discover which
        // identifiers exist.
        response.StatusCode.ShouldBe(HttpStatusCode.Forbidden);
    }
}
