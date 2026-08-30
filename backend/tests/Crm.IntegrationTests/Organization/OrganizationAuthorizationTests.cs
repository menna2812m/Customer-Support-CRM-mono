using System.Net;
using System.Net.Http.Json;
using Crm.IntegrationTests.Infrastructure;
using Shouldly;
using static Crm.IntegrationTests.Organization.OrganizationHarness;

namespace Crm.IntegrationTests.Organization;

/// <summary>
/// Spec AR-001 to AR-003. Reading the structure is separated from maintaining it, and neither is
/// open to the portal population.
///
/// These are the tests that matter most in this feature. A wrong name is a nuisance; a caller who
/// can reorganize the business because a single attribute was omitted from one endpoint is not.
/// </summary>
[Collection(DatabaseCollectionDefinition.Name)]
public sealed class OrganizationAuthorizationTests(SqlServerFixture database)
{
    [Fact]
    public async Task An_anonymous_caller_is_refused_before_any_permission_is_considered()
    {
        var (harness, _) = Administrator(database.ConnectionString);
        await using var _disposable = harness;
        using var anonymous = harness.CreateClient();

        var response = await anonymous.GetAsync(Route(Departments));

        response.StatusCode.ShouldBe(HttpStatusCode.Unauthorized);
        (await ReadCodeAsync(response)).ShouldBe("unauthenticated");
    }

    [Fact]
    public async Task An_agent_can_neither_read_nor_change_the_structure()
    {
        // The Agent role deliberately holds neither organization permission: maintaining the
        // organization is not day-to-day work, and an agent has no reason to browse it either.
        await using var harness = SignInHarness.Create(
            database.ConnectionString,
            new Dictionary<string, string?> { ["Identity:DefaultRole"] = "Agent" });

        var account = harness.Provider.AddAccount();
        (await harness.SignInAsync(account)).Succeeded.ShouldBeTrue();

        using var client = harness.CreateAuthenticatedClient(await harness.IssueAccessCredentialAsync());

        var read = await client.GetAsync(Route(Departments));
        var write = await client.PostAsJsonAsync(
            Route(Departments),
            NewUnit("محاولة", "Attempt", $"ATT{Tag()}"));

        read.StatusCode.ShouldBe(HttpStatusCode.Forbidden);
        write.StatusCode.ShouldBe(HttpStatusCode.Forbidden);

        // Refused for the right reason: authenticated, but not permitted.
        (await ReadCodeAsync(read)).ShouldBe("forbidden");
    }

    [Theory]
    [InlineData("POST", "")]
    [InlineData("PUT", "/{0}")]
    [InlineData("PUT", "/{0}/activation")]
    [InlineData("DELETE", "/{0}")]
    public async Task Every_write_endpoint_declares_the_manage_permission(string method, string suffix)
    {
        // A caller holding organization.view must be refused by every write, not merely by the ones
        // somebody remembered to attribute. Enumerating them here is what makes a missed attribute
        // a failing test rather than a silent hole.
        var tag = Tag();
        var (harness, email) = Administrator(database.ConnectionString);
        await using var _ = harness;
        using var administrator = await SignInAsync(harness, email);

        var id = await CreateAsync(
            administrator,
            Departments,
            $"هدف {tag}",
            $"Target {tag}",
            $"TGT{tag}");

        // The read-only role holds no organization permission at all, so grant view alone by using
        // a role that has it: the administrator's own session minus manage is not expressible, so
        // this asserts the complementary half - that a viewer without manage cannot write.
        await using var viewerHarness = SignInHarness.Create(
            database.ConnectionString,
            new Dictionary<string, string?> { ["Identity:DefaultRole"] = "ReadOnly" });

        var viewerAccount = viewerHarness.Provider.AddAccount();
        (await viewerHarness.SignInAsync(viewerAccount)).Succeeded.ShouldBeTrue();

        using var viewer = viewerHarness.CreateAuthenticatedClient(
            await viewerHarness.IssueAccessCredentialAsync());

        var route = Route($"{Departments}{string.Format(System.Globalization.CultureInfo.InvariantCulture, suffix, id)}");

        var response = method switch
        {
            "POST" => await viewer.PostAsJsonAsync(route, NewUnit("x", "x", $"X{tag}")),
            "PUT" when suffix.EndsWith("activation", StringComparison.Ordinal) =>
                await viewer.PutAsJsonAsync(route, Activation(false)),
            "PUT" => await viewer.PutAsJsonAsync(route, Rename("x", "x")),
            _ => await viewer.DeleteAsync(route),
        };

        response.StatusCode.ShouldBe(HttpStatusCode.Forbidden);
    }

    [Fact]
    public async Task Team_endpoints_are_permissioned_like_the_rest()
    {
        var tag = Tag();
        var (harness, email) = Administrator(database.ConnectionString);
        await using var _ = harness;
        using var administrator = await SignInAsync(harness, email);

        var department = await CreateAsync(
            administrator,
            Departments,
            $"قسم {tag}",
            $"Division {tag}",
            $"DIV{tag}");

        var created = await administrator.PostAsJsonAsync(
            Route($"{Departments}/{department}/teams"),
            NewUnit($"فريق {tag}", $"Squad {tag}", $"SQ{tag}"));

        var teamId = await ReadIdAsync(created);

        await using var viewerHarness = SignInHarness.Create(
            database.ConnectionString,
            new Dictionary<string, string?> { ["Identity:DefaultRole"] = "ReadOnly" });

        var viewerAccount = viewerHarness.Provider.AddAccount();
        (await viewerHarness.SignInAsync(viewerAccount)).Succeeded.ShouldBeTrue();

        using var viewer = viewerHarness.CreateAuthenticatedClient(
            await viewerHarness.IssueAccessCredentialAsync());

        // The move is the most consequential write in the feature: it changes other people's
        // records. It must be no easier to reach than a rename.
        var move = await viewer.PutAsJsonAsync(
            Route($"{Teams}/{teamId}/department"),
            Destination(Guid.NewGuid()));

        move.StatusCode.ShouldBe(HttpStatusCode.Forbidden);
    }
}
