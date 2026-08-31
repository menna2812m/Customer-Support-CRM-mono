using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using Crm.IntegrationTests.Infrastructure;
using Shouldly;
using static Crm.IntegrationTests.Organization.OrganizationHarness;

namespace Crm.IntegrationTests.Identity;

/// <summary>
/// Creating somebody before they arrive (spec FR-013, FR-014).
/// </summary>
/// <remarks>
/// PeopleEndpointsTests already refuses an address belonging to somebody who has signed in. These
/// are the cases it does not reach: an address held by a record that is only prepared, an address
/// spelled differently from the way it is stored, and everything arranged at once. The first two are
/// where "already in use" is least obvious to the person typing it.
/// </remarks>
[Collection(DatabaseCollectionDefinition.Name)]
public sealed class PreProvisionEndpointTests(SqlServerFixture database)
{
    private const string People = "/api/v1/identity/people";
    private const string Roles = "/api/v1/identity/roles";
    private const string Branches = "/api/v1/organization/branches";

    [Fact]
    public async Task An_address_that_already_belongs_to_somebody_is_refused()
    {
        var (harness, email) = Administrator(database.ConnectionString);
        await using var _ = harness;
        using var client = await SignInAsync(harness, email);

        var address = $"{Tag()}@prepared.local";

        (await client.PostAsJsonAsync(Route(People), new { email = address, displayName = "First" }))
            .StatusCode.ShouldBe(HttpStatusCode.Created);

        var again = await client.PostAsJsonAsync(
            Route(People),
            new { email = address, displayName = "Second" });

        again.StatusCode.ShouldBe(HttpStatusCode.Conflict);

        var problem = await again.Content.ReadAsStringAsync();
        problem.ShouldContain("identity_email_in_use");
    }

    [Fact]
    public async Task An_address_differing_only_in_case_is_the_same_address()
    {
        var (harness, email) = Administrator(database.ConnectionString);
        await using var _ = harness;
        using var client = await SignInAsync(harness, email);

        var address = $"{Tag()}@prepared.local";

        (await client.PostAsJsonAsync(Route(People), new { email = address, displayName = "First" }))
            .StatusCode.ShouldBe(HttpStatusCode.Created);

        // Normalization is what makes the uniqueness rule mean anything. Without it, two spellings
        // of one address become two people, and the claim on first sign-in becomes ambiguous.
        var shouted = await client.PostAsJsonAsync(
            Route(People),
            new { email = address.ToUpperInvariant(), displayName = "Second" });

        shouted.StatusCode.ShouldBe(HttpStatusCode.Conflict);
    }

    [Fact]
    public async Task A_person_can_be_prepared_holding_a_role_and_a_placement_immediately()
    {
        var (harness, email) = Administrator(database.ConnectionString);
        await using var _ = harness;
        using var client = await SignInAsync(harness, email);

        var branchId = await CreateBranchAsync(client);
        var roleId = await FindRoleAsync(client, "Agent");

        var response = await client.PostAsJsonAsync(
            Route(People),
            new
            {
                email = $"{Tag()}@prepared.local",
                displayName = "Prepared Person",
                roleIds = new[] { roleId },
                placement = new { branchId },
            });

        // FR-013: everything arranged in one call, so an administrator preparing somebody on Friday
        // does not have to remember to finish it on Monday.
        response.StatusCode.ShouldBe(HttpStatusCode.Created);

        using var document = JsonDocument.Parse(await response.Content.ReadAsStringAsync());
        var body = document.RootElement;

        body.GetProperty("summary").GetProperty("status").GetString().ShouldBe("invited", StringCompareShould.IgnoreCase);
        body.GetProperty("summary").GetProperty("placement").GetProperty("branchId").GetGuid().ShouldBe(branchId);

        body.GetProperty("roles")
            .EnumerateArray()
            .Select(role => role.GetProperty("name").GetString())
            .ShouldContain("Agent");
    }

    private static async Task<Guid> CreateBranchAsync(HttpClient client)
    {
        var tag = Tag();

        var response = await client.PostAsJsonAsync(
            Route(Branches),
            NewUnit($"فرع {tag}", $"Branch {tag}", $"BR-{tag}"));

        response.EnsureSuccessStatusCode();

        using var document = JsonDocument.Parse(await response.Content.ReadAsStringAsync());

        return document.RootElement.GetProperty("id").GetGuid();
    }

    private static async Task<Guid> FindRoleAsync(HttpClient client, string name)
    {
        var response = await client.GetAsync(Route(Roles));
        using var document = JsonDocument.Parse(await response.Content.ReadAsStringAsync());

        return document.RootElement
            .EnumerateArray()
            .First(role => role.GetProperty("name").GetString() == name)
            .GetProperty("id")
            .GetGuid();
    }
}
