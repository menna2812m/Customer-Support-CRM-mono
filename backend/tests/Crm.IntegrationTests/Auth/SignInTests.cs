using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using Crm.Application.Authorization;
using Crm.Domain.Identity;
using Crm.Infrastructure.Persistence;
using Crm.IntegrationTests.Infrastructure;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Shouldly;

namespace Crm.IntegrationTests.Auth;

/// <summary>
/// User Story 1, verified through the real handshake against the in-process provider: PKCE, code
/// exchange, identity-token validation, provisioning, and the credential the API then issues.
/// </summary>
[Collection(DatabaseCollectionDefinition.Name)]
public sealed class SignInTests(SqlServerFixture database)
{
    [Fact]
    public async Task A_staff_member_signs_in_and_reaches_an_endpoint_that_previously_refused_them()
    {
        var email = $"{Guid.CreateVersion7():n}@fake.local";

        // The bootstrap administrator, because the only protected endpoint the foundation ships is
        // the diagnostics surface, which the Agent role deliberately cannot reach.
        await using var harness = SignInHarness.Create(
            database.ConnectionString,
            new Dictionary<string, string?> { ["Identity:BootstrapAdministrator"] = email });

        var account = harness.Provider.AddAccount(email: email, displayName: "Layla Hassan");

        using (var anonymous = harness.CreateClient())
        {
            var refused = await anonymous.GetAsync(new Uri("/api/v1/diagnostics/items", UriKind.Relative));

            // Establishes the "previously" in this test's name rather than asserting it in a comment.
            refused.StatusCode.ShouldBe(HttpStatusCode.Unauthorized);
        }

        var result = await harness.SignInAsync(account);

        result.Succeeded.ShouldBeTrue($"sign-in failed with '{result.Error}'");

        // Nothing secret may travel in the URL: the credential arrives through the session call.
        result.RedirectedTo!.ToString().ShouldNotContain(result.RenewalCookie!);
        result.RedirectedTo.Query.ShouldNotContain("token");

        // The destination survives the provider round trip, so a person who followed a link to a
        // ticket lands on that ticket rather than on a home page (spec FR-034).
        result.RedirectedTo.Query.ShouldContain(Uri.EscapeDataString("/tickets/42"));

        var session = await harness.RequestSessionAsync();
        session.StatusCode.ShouldBe(HttpStatusCode.OK);

        using var document = JsonDocument.Parse(await session.Content.ReadAsStringAsync());
        var accessToken = document.RootElement.GetProperty("accessToken").GetString()!;

        document.RootElement.GetProperty("user").GetProperty("displayName").GetString().ShouldBe("Layla Hassan");

        using var client = harness.CreateAuthenticatedClient(accessToken);
        var response = await client.GetAsync(new Uri("/api/v1/diagnostics/items", UriKind.Relative));

        response.StatusCode.ShouldBe(HttpStatusCode.OK);
    }

    [Fact]
    public async Task The_first_sign_in_provisions_a_user_and_the_second_reuses_it()
    {
        await using var harness = SignInHarness.Create(database.ConnectionString);
        var account = harness.Provider.AddAccount();

        var first = await harness.SignInAsync(account);
        first.Succeeded.ShouldBeTrue();

        var userId = await harness.GetUserIdAsync(account.Subject);

        var second = await harness.SignInAsync(account);
        second.Succeeded.ShouldBeTrue();

        // Identity is keyed on the provider subject, so a returning person is the same record.
        (await harness.GetUserIdAsync(account.Subject)).ShouldBe(userId);
    }

    [Fact]
    public async Task A_new_subject_carrying_an_existing_email_is_refused_and_recorded()
    {
        await using var harness = SignInHarness.Create(database.ConnectionString);

        const string sharedEmail = "reissued.address@fake.local";
        var existingUserId = await harness.SeedUserAsync("original|subject", sharedEmail);

        // The new hire who inherited a leaver's email address.
        var account = harness.Provider.AddAccount(subject: "new|subject", email: sharedEmail);

        var result = await harness.SignInAsync(account);

        result.Error.ShouldBe("identity_collision");
        result.RenewalCookie.ShouldBeNull();

        using var scope = harness.Services.CreateScope();
        var context = scope.ServiceProvider.GetRequiredService<CrmDbContext>();

        // Neither linked nor duplicated: no second user, and the existing one is untouched.
        (await context.Users.CountAsync(user => user.Email == sharedEmail)).ShouldBe(1);
        (await context.Users.AnyAsync(user => user.ProviderSubject == "new|subject")).ShouldBeFalse();

        var events = await harness.GetEventsAsync("new|subject");
        var collision = events.ShouldHaveSingleItem();
        collision.Action.ShouldBe(AuthenticationActions.SignInCollision);
        collision.UserId.ShouldBe(existingUserId);
        collision.SubjectReference.ShouldBe("new|subject");
    }

    [Fact]
    public async Task A_user_with_no_role_is_told_they_have_no_access()
    {
        await using var harness = SignInHarness.Create(
            database.ConnectionString,
            new Dictionary<string, string?> { ["Identity:DefaultRole"] = string.Empty });

        var account = harness.Provider.AddAccount();

        var result = await harness.SignInAsync(account);

        // Recognised, provisioned, and granted nothing - distinct from a failed sign-in.
        result.Error.ShouldBe("no_access");
        result.RenewalCookie.ShouldBeNull();

        (await harness.GetUserIdAsync(account.Subject)).ShouldNotBe(Guid.Empty);
    }

    [Fact]
    public async Task An_inactive_user_is_refused_even_though_the_provider_authenticated_them()
    {
        await using var harness = SignInHarness.Create(database.ConnectionString);

        await harness.SeedUserAsync("inactive|subject", "inactive@fake.local", isActive: false);
        var account = harness.Provider.AddAccount(subject: "inactive|subject", email: "inactive@fake.local");

        var result = await harness.SignInAsync(account);

        result.Error.ShouldBe("no_access");
        result.RenewalCookie.ShouldBeNull();
    }

    [Fact]
    public async Task A_first_time_staff_member_receives_the_configured_default_role()
    {
        await using var harness = SignInHarness.Create(database.ConnectionString);
        var account = harness.Provider.AddAccount();

        (await harness.SignInAsync(account)).Succeeded.ShouldBeTrue();

        var session = await harness.RequestSessionAsync();
        using var document = JsonDocument.Parse(await session.Content.ReadAsStringAsync());

        var permissions = document.RootElement
            .GetProperty("user")
            .GetProperty("permissions")
            .EnumerateArray()
            .Select(entry => entry.GetString())
            .ToList();

        // The Agent role, which is what makes the CRM usable by a team before the administration
        // screens exist.
        permissions.ShouldContain(Permissions.Tickets.Create);
        permissions.ShouldNotContain(Permissions.Users.Manage);
    }

    [Fact]
    public async Task The_configured_bootstrap_administrator_holds_every_permission()
    {
        var email = $"{Guid.CreateVersion7():n}@fake.local";

        await using var harness = SignInHarness.Create(
            database.ConnectionString,
            new Dictionary<string, string?>
            {
                ["Identity:BootstrapAdministrator"] = email,
                ["Identity:DefaultRole"] = "ReadOnly",
            });

        var account = harness.Provider.AddAccount(email: email);

        (await harness.SignInAsync(account)).Succeeded.ShouldBeTrue();

        var session = await harness.RequestSessionAsync();
        using var document = JsonDocument.Parse(await session.Content.ReadAsStringAsync());

        var permissions = document.RootElement
            .GetProperty("user")
            .GetProperty("permissions")
            .EnumerateArray()
            .Select(entry => entry.GetString()!)
            .ToHashSet(StringComparer.Ordinal);

        // Without this, a fresh deployment authenticates people successfully and admits nobody -
        // with no way in (spec SC-007).
        permissions.ShouldBe(Permissions.All, ignoreOrder: true);
    }

    [Fact]
    public async Task Placement_asserted_by_the_provider_reaches_the_session()
    {
        var department = Guid.CreateVersion7();

        await using var harness = SignInHarness.Create(database.ConnectionString);
        var account = harness.Provider.AddAccount(departmentId: department);

        (await harness.SignInAsync(account)).Succeeded.ShouldBeTrue();

        var session = await harness.RequestSessionAsync();
        using var document = JsonDocument.Parse(await session.Content.ReadAsStringAsync());

        document.RootElement
            .GetProperty("user")
            .GetProperty("scope")
            .GetProperty("departmentId")
            .GetString()
            .ShouldBe(department.ToString());
    }

    [Fact]
    public async Task A_provider_that_cannot_be_reached_is_reported_as_a_provider_problem()
    {
        await using var harness = SignInHarness.Create(database.ConnectionString);
        harness.Provider.IsUnavailable = true;

        using var client = harness.CreateAuthenticatedClient("not-used");
        var response = await client.GetAsync(new Uri("/api/v1/auth/sign-in", UriKind.Relative));

        response.StatusCode.ShouldBe(HttpStatusCode.ServiceUnavailable);

        using var document = JsonDocument.Parse(await response.Content.ReadAsStringAsync());
        document.RootElement.GetProperty("code").GetString().ShouldBe("provider_unavailable");

        // Blaming the user for a provider outage wastes their time and support's.
        (document.RootElement.GetProperty("title").GetString() ?? string.Empty).ShouldNotContain("credential");
    }

    [Theory]
    [InlineData("https://evil.example/steal")]
    [InlineData("//evil.example")]
    [InlineData("/\\evil.example")]
    [InlineData("javascript:alert(1)")]
    public async Task A_return_path_pointing_anywhere_but_this_application_is_refused(string returnUrl)
    {
        await using var harness = SignInHarness.Create(database.ConnectionString);

        using var client = harness.CreateAuthenticatedClient("not-used");
        var response = await client.GetAsync(
            new Uri($"/api/v1/auth/sign-in?returnUrl={Uri.EscapeDataString(returnUrl)}", UriKind.Relative));

        // The redirect goes to the provider; what matters is that the hostile value never becomes
        // the destination the user lands on afterwards.
        response.Headers.Location?.ToString().ShouldNotContain("evil.example");
    }
}
