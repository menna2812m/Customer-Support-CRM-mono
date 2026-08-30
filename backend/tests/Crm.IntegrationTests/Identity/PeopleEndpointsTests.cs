using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using Crm.IntegrationTests.Infrastructure;
using Shouldly;
using static Crm.IntegrationTests.Organization.OrganizationHarness;

namespace Crm.IntegrationTests.Identity;

/// <summary>
/// The people endpoints (spec FR-001 to FR-014, FR-022 to FR-030).
///
/// The negative cases carry the weight, as they did in feature 003. Listing people is not where
/// this can go wrong; refusing a duplicate address, refusing a placement that would break INV-2,
/// and refusing a change that would leave nobody able to administer the system are.
/// </summary>
[Collection(DatabaseCollectionDefinition.Name)]
public sealed class PeopleEndpointsTests(SqlServerFixture database)
{
    private const string People = "/api/v1/identity/people";
    private const string Roles = "/api/v1/identity/roles";

    [Fact]
    public async Task The_signed_in_administrator_appears_in_the_list()
    {
        var (harness, email) = Administrator(database.ConnectionString);
        await using var _ = harness;
        using var client = await SignInAsync(harness, email);

        var response = await client.GetAsync(Route($"{People}?page=1&pageSize=50"));

        response.StatusCode.ShouldBe(HttpStatusCode.OK);

        var body = await response.Content.ReadAsStringAsync();
        body.ShouldContain(email);

        // Somebody who has signed in is active, not invited - the status is derived from whether an
        // identity is bound, and theirs is.
        body.ShouldContain("\"status\":\"active\"", Case.Insensitive);
    }

    [Fact]
    public async Task A_person_can_be_prepared_by_email_and_appears_as_invited()
    {
        var (harness, email) = Administrator(database.ConnectionString);
        await using var _ = harness;
        using var client = await SignInAsync(harness, email);

        var prepared = $"{Tag()}@prepared.local";

        var response = await client.PostAsJsonAsync(
            Route(People),
            new { email = prepared, displayName = "Prepared Person" });

        response.StatusCode.ShouldBe(HttpStatusCode.Created);

        var body = await response.Content.ReadAsStringAsync();
        body.ShouldContain(prepared);
        body.ShouldContain("\"status\":\"invited\"", Case.Insensitive);
        body.ShouldContain("\"hasSignedIn\":false", Case.Insensitive);
    }

    [Fact]
    public async Task Preparing_an_address_that_already_belongs_to_someone_is_refused()
    {
        var (harness, email) = Administrator(database.ConnectionString);
        await using var _ = harness;
        using var client = await SignInAsync(harness, email);

        var response = await client.PostAsJsonAsync(
            Route(People),
            new { email, displayName = "Duplicate" });

        response.StatusCode.ShouldBe(HttpStatusCode.Conflict);
        (await ReadCodeAsync(response)).ShouldBe("identity_email_in_use");
    }

    [Fact]
    public async Task The_never_signed_in_filter_finds_only_prepared_people()
    {
        var (harness, email) = Administrator(database.ConnectionString);
        await using var _ = harness;
        using var client = await SignInAsync(harness, email);

        var prepared = $"{Tag()}@prepared.local";
        await client.PostAsJsonAsync(Route(People), new { email = prepared, displayName = "Prepared" });

        var response = await client.GetAsync(Route($"{People}?unlinkedOnly=true&pageSize=50"));
        var body = await response.Content.ReadAsStringAsync();

        // The question pre-provisioning creates: who has been prepared and not yet arrived.
        body.ShouldContain(prepared);
        body.ShouldNotContain(email);
    }

    [Fact]
    public async Task A_role_can_be_granted_and_revoked_and_the_permissions_follow()
    {
        var (harness, email) = Administrator(database.ConnectionString);
        await using var _ = harness;
        using var client = await SignInAsync(harness, email);

        var personId = await PrepareAsync(client);
        var agentRoleId = await FindRoleAsync(client, "Agent");

        var granted = await client.PostAsync(Route($"{People}/{personId}/roles/{agentRoleId}"), null);
        granted.StatusCode.ShouldBe(HttpStatusCode.OK);
        (await granted.Content.ReadAsStringAsync()).ShouldContain("customers.create");

        var revoked = await client.DeleteAsync(Route($"{People}/{personId}/roles/{agentRoleId}"));
        revoked.StatusCode.ShouldBe(HttpStatusCode.OK);
        (await revoked.Content.ReadAsStringAsync()).ShouldNotContain("customers.create");
    }

    [Fact]
    public async Task Granting_a_role_twice_is_accepted_and_leaves_one_grant()
    {
        var (harness, email) = Administrator(database.ConnectionString);
        await using var _ = harness;
        using var client = await SignInAsync(harness, email);

        var personId = await PrepareAsync(client);
        var agentRoleId = await FindRoleAsync(client, "Agent");

        await client.PostAsync(Route($"{People}/{personId}/roles/{agentRoleId}"), null);
        var second = await client.PostAsync(Route($"{People}/{personId}/roles/{agentRoleId}"), null);

        second.StatusCode.ShouldBe(HttpStatusCode.OK);

        using var document = JsonDocument.Parse(await second.Content.ReadAsStringAsync());
        document.RootElement.GetProperty("roles").GetArrayLength().ShouldBe(1);
    }

    [Fact]
    public async Task An_administrator_cannot_remove_their_own_administrator_role()
    {
        var (harness, email) = Administrator(database.ConnectionString);
        await using var _ = harness;
        using var client = await SignInAsync(harness, email);

        var me = await FindSelfAsync(client, email);
        var administratorRoleId = await FindRoleAsync(client, "Administrator");

        var response = await client.DeleteAsync(Route($"{People}/{me}/roles/{administratorRoleId}"));

        response.StatusCode.ShouldBe(HttpStatusCode.Conflict);

        // Either guard may be the one that refuses, and which one depends on the shared database:
        // every test in this suite bootstraps its own administrator, so whether others exist is a
        // function of what else has run. Both codes are correct answers to "you may not do this to
        // yourself"; which one is reported when both apply is pinned in AdministratorGuardTests,
        // and enforced against an exactly-known fixture in PeopleStoreTests.
        (await ReadCodeAsync(response)).ShouldBeOneOf(
            "identity_last_administrator",
            "identity_self_demotion");
    }

    [Fact]
    public async Task An_administrator_cannot_delete_their_own_account()
    {
        var (harness, email) = Administrator(database.ConnectionString);
        await using var _ = harness;
        using var client = await SignInAsync(harness, email);

        var me = await FindSelfAsync(client, email);

        var response = await client.DeleteAsync(Route($"{People}/{me}"));

        response.StatusCode.ShouldBe(HttpStatusCode.Conflict);
    }

    [Fact]
    public async Task A_prepared_person_can_be_deleted_and_their_address_reused()
    {
        var (harness, email) = Administrator(database.ConnectionString);
        await using var _ = harness;
        using var client = await SignInAsync(harness, email);

        var address = $"{Tag()}@prepared.local";
        var created = await client.PostAsJsonAsync(
            Route(People),
            new { email = address, displayName = "Temporary" });

        var personId = await ReadIdentifierAsync(created);

        (await client.DeleteAsync(Route($"{People}/{personId}"))).StatusCode
            .ShouldBe(HttpStatusCode.NoContent);

        // Deleting frees the address, which is why the email index is filtered (FR-026).
        var again = await client.PostAsJsonAsync(
            Route(People),
            new { email = address, displayName = "Replacement" });

        again.StatusCode.ShouldBe(HttpStatusCode.Created);
    }

    [Fact]
    public async Task A_page_size_over_the_maximum_is_a_validation_failure()
    {
        var (harness, email) = Administrator(database.ConnectionString);
        await using var _ = harness;
        using var client = await SignInAsync(harness, email);

        var response = await client.GetAsync(Route($"{People}?pageSize=500"));

        response.StatusCode.ShouldBe(HttpStatusCode.BadRequest);
    }

    [Fact]
    public async Task An_unknown_query_parameter_is_refused_rather_than_ignored()
    {
        var (harness, email) = Administrator(database.ConnectionString);
        await using var _ = harness;
        using var client = await SignInAsync(harness, email);

        var response = await client.GetAsync(Route($"{People}?departmentID=abc"));

        response.StatusCode.ShouldBe(HttpStatusCode.BadRequest);
    }

    [Fact]
    public async Task The_roles_list_carries_the_permissions_each_role_grants()
    {
        var (harness, email) = Administrator(database.ConnectionString);
        await using var _ = harness;
        using var client = await SignInAsync(harness, email);

        var response = await client.GetAsync(Route(Roles));

        response.StatusCode.ShouldBe(HttpStatusCode.OK);

        var body = await response.Content.ReadAsStringAsync();
        body.ShouldContain("Administrator");

        // Carried so the interface can show effective permissions as derived rather than editable.
        body.ShouldContain("identity.manage");
    }

    private static async Task<Guid> PrepareAsync(HttpClient client)
    {
        var response = await client.PostAsJsonAsync(
            Route(People),
            new { email = $"{Tag()}@prepared.local", displayName = "Prepared" });

        response.EnsureSuccessStatusCode();

        return await ReadIdentifierAsync(response);
    }

    private static async Task<Guid> ReadIdentifierAsync(HttpResponseMessage response)
    {
        using var document = JsonDocument.Parse(await response.Content.ReadAsStringAsync());

        return document.RootElement.GetProperty("summary").GetProperty("id").GetGuid();
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

    private static async Task<Guid> FindSelfAsync(HttpClient client, string email)
    {
        var response = await client.GetAsync(Route($"{People}?search={Uri.EscapeDataString(email)}"));
        using var document = JsonDocument.Parse(await response.Content.ReadAsStringAsync());

        return document.RootElement
            .GetProperty("items")
            .EnumerateArray()
            .First()
            .GetProperty("id")
            .GetGuid();
    }
}
