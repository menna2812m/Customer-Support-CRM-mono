using System.Net;
using System.Net.Http.Json;
using Crm.IntegrationTests.Infrastructure;
using Shouldly;
using static Crm.IntegrationTests.Organization.OrganizationHarness;

namespace Crm.IntegrationTests.Identity;

/// <summary>
/// Spec FR-023 and SC-005: access ends on the next request, not when a credential expires.
/// </summary>
/// <remarks>
/// This is the test the whole promise rests on. A self-contained access credential lives for
/// fifteen minutes (<c>AccessCredentialMinutes</c>), so "immediately" would be a fifteen-minute lie
/// if token validation trusted the credential alone. It does not - it resolves the session claim
/// against the session store on every request - and this proves it with a credential that is still
/// well within its lifetime.
///
/// Two harnesses share one database because each holds a single session. That is what lets one
/// person be signed in while another administers them.
/// </remarks>
[Collection(DatabaseCollectionDefinition.Name)]
public sealed class SessionEndingTests(SqlServerFixture database)
{
    private const string People = "/api/v1/identity/people";

    [Fact]
    public async Task Deactivating_somebody_ends_their_session_on_their_very_next_request()
    {
        // The person being deactivated: signed in, holding a live credential.
        await using var theirs = SignInHarness.Create(
            database.ConnectionString,
            new Dictionary<string, string?> { ["Identity:DefaultRole"] = "Agent" });

        var account = theirs.Provider.AddAccount();
        (await theirs.SignInAsync(account)).Succeeded.ShouldBeTrue();

        using var theirClient = theirs.CreateAuthenticatedClient(await theirs.IssueAccessCredentialAsync());
        var theirUserId = await theirs.GetUserIdAsync(account.Subject);

        // Before: authenticated, and refused only because an agent may not administer people. The
        // distinction between 403 and 401 is what makes the assertion afterwards meaningful.
        var before = await theirClient.GetAsync(Route(People));
        before.StatusCode.ShouldBe(HttpStatusCode.Forbidden);

        // An administrator, elsewhere, deactivates them.
        var (adminHarness, adminEmail) = Administrator(database.ConnectionString);
        await using var _ = adminHarness;
        using var adminClient = await SignInAsync(adminHarness, adminEmail);

        var deactivated = await adminClient.PutAsJsonAsync(
            Route($"{People}/{theirUserId}/activation"),
            new { isActive = false });

        deactivated.StatusCode.ShouldBe(HttpStatusCode.OK);

        // After: the same credential, still nowhere near expiry, is no longer accepted at all.
        var after = await theirClient.GetAsync(Route(People));

        after.StatusCode.ShouldBe(HttpStatusCode.Unauthorized);
        (await ReadCodeAsync(after)).ShouldBe("unauthenticated");
    }

    [Fact]
    public async Task Reactivating_somebody_does_not_bring_their_old_session_back()
    {
        await using var theirs = SignInHarness.Create(
            database.ConnectionString,
            new Dictionary<string, string?> { ["Identity:DefaultRole"] = "Agent" });

        var account = theirs.Provider.AddAccount();
        await theirs.SignInAsync(account);

        using var theirClient = theirs.CreateAuthenticatedClient(await theirs.IssueAccessCredentialAsync());
        var theirUserId = await theirs.GetUserIdAsync(account.Subject);

        var (adminHarness, adminEmail) = Administrator(database.ConnectionString);
        await using var _ = adminHarness;
        using var adminClient = await SignInAsync(adminHarness, adminEmail);

        await adminClient.PutAsJsonAsync(Route($"{People}/{theirUserId}/activation"), new { isActive = false });
        await adminClient.PutAsJsonAsync(Route($"{People}/{theirUserId}/activation"), new { isActive = true });

        // Revocation is not undone by reactivation: the person may sign in again, but the session
        // that was ended stays ended. Anything else would make revocation temporary.
        var after = await theirClient.GetAsync(Route(People));

        after.StatusCode.ShouldBe(HttpStatusCode.Unauthorized);
    }
}
