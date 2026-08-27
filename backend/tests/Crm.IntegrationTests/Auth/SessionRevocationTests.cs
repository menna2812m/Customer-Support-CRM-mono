using System.Net;
using System.Net.Http.Json;
using Crm.Application.Identity;
using Crm.Domain.Identity;
using Crm.IntegrationTests.Infrastructure;
using Microsoft.Extensions.DependencyInjection;
using Shouldly;

namespace Crm.IntegrationTests.Auth;

/// <summary>
/// What "ending access" means when a person is signed in more than once (spec FR-014, FR-015,
/// FR-020).
///
/// One person on a laptop and a phone is two sessions, and the difference between ending one and
/// ending all of them is a difference the user chooses. Deactivation is not a choice they make:
/// it ends everything, at once, because the reason for deactivating somebody is usually that they
/// should not still be working.
/// </summary>
[Collection(DatabaseCollectionDefinition.Name)]
public sealed class SessionRevocationTests(SqlServerFixture database)
{
    [Fact]
    public async Task Two_sessions_are_independent_and_signing_out_of_one_leaves_the_other_working()
    {
        var email = $"{Guid.CreateVersion7():n}@fake.local";

        // Two harnesses over one application would be two applications; one harness with two cookie
        // jars is the honest model of the same person on two devices, so the sessions are started
        // through the store and the credentials issued directly.
        await using var harness = SignInHarness.Create(
            database.ConnectionString,
            new Dictionary<string, string?> { ["Identity:BootstrapAdministrator"] = email });

        var account = harness.Provider.AddAccount(email: email);
        (await harness.SignInAsync(account)).Succeeded.ShouldBeTrue();

        var userId = await harness.GetUserIdAsync(account.Subject);

        var laptop = await harness.IssueAccessCredentialAsync();
        var phone = await IssueSecondSessionAsync(harness, userId);

        using var laptopClient = harness.CreateAuthenticatedClient(laptop);
        using var phoneClient = harness.CreateAuthenticatedClient(phone.AccessCredential);

        (await laptopClient.GetAsync(Diagnostics)).StatusCode.ShouldBe(HttpStatusCode.OK);
        (await phoneClient.GetAsync(Diagnostics)).StatusCode.ShouldBe(HttpStatusCode.OK);

        await laptopClient.PostAsJsonAsync(SignOut, new { allSessions = false, endProviderSession = false });

        (await laptopClient.GetAsync(Diagnostics)).StatusCode.ShouldBe(HttpStatusCode.Unauthorized);

        // Signing out on the laptop must not sign the phone out. Anything else makes "sign out"
        // unusable on a shared machine, because it would strand the user everywhere else.
        (await phoneClient.GetAsync(Diagnostics)).StatusCode.ShouldBe(HttpStatusCode.OK);
    }

    [Fact]
    public async Task Signing_out_everywhere_ends_every_session_the_user_holds()
    {
        var email = $"{Guid.CreateVersion7():n}@fake.local";

        await using var harness = SignInHarness.Create(
            database.ConnectionString,
            new Dictionary<string, string?> { ["Identity:BootstrapAdministrator"] = email });

        var account = harness.Provider.AddAccount(email: email);
        (await harness.SignInAsync(account)).Succeeded.ShouldBeTrue();

        var userId = await harness.GetUserIdAsync(account.Subject);

        var laptop = await harness.IssueAccessCredentialAsync();
        var phone = await IssueSecondSessionAsync(harness, userId);

        using var laptopClient = harness.CreateAuthenticatedClient(laptop);
        using var phoneClient = harness.CreateAuthenticatedClient(phone.AccessCredential);

        await laptopClient.PostAsJsonAsync(SignOut, new { allSessions = true, endProviderSession = false });

        (await laptopClient.GetAsync(Diagnostics)).StatusCode.ShouldBe(HttpStatusCode.Unauthorized);

        // The point of the option: a device the person no longer has is signed out from one they do.
        (await phoneClient.GetAsync(Diagnostics)).StatusCode.ShouldBe(HttpStatusCode.Unauthorized);

        var sessions = await harness.GetSessionsForUserAsync(userId);

        sessions.ShouldAllBe(session => session.IsRevoked);
    }

    [Fact]
    public async Task Deactivating_a_user_ends_every_live_session_at_once()
    {
        var email = $"{Guid.CreateVersion7():n}@fake.local";

        await using var harness = SignInHarness.Create(
            database.ConnectionString,
            new Dictionary<string, string?> { ["Identity:BootstrapAdministrator"] = email });

        var account = harness.Provider.AddAccount(email: email);
        (await harness.SignInAsync(account)).Succeeded.ShouldBeTrue();

        var userId = await harness.GetUserIdAsync(account.Subject);

        var laptop = await harness.IssueAccessCredentialAsync();
        var phone = await IssueSecondSessionAsync(harness, userId);

        using var laptopClient = harness.CreateAuthenticatedClient(laptop);
        using var phoneClient = harness.CreateAuthenticatedClient(phone.AccessCredential);

        (await laptopClient.GetAsync(Diagnostics)).StatusCode.ShouldBe(HttpStatusCode.OK);

        var revoked = await harness.WithServicesAsync(services =>
            services.GetRequiredService<DeactivateUser>().ExecuteAsync(userId));

        revoked.ShouldBe(2);

        // Deactivation that left a credential working for another quarter of an hour would be a
        // decision that does not take effect when it is made.
        (await laptopClient.GetAsync(Diagnostics)).StatusCode.ShouldBe(HttpStatusCode.Unauthorized);
        (await phoneClient.GetAsync(Diagnostics)).StatusCode.ShouldBe(HttpStatusCode.Unauthorized);

        var sessions = await harness.GetSessionsForUserAsync(userId);

        sessions.ShouldAllBe(session => session.RevokedReason == SessionRevocationReason.UserDeactivated);

        // And the renewal cookie the browser still holds buys nothing back.
        using var renewed = await harness.RequestSessionAsync();
        renewed.StatusCode.ShouldBe(HttpStatusCode.Unauthorized);
    }

    [Fact]
    public async Task Deactivating_an_already_inactive_user_changes_nothing()
    {
        await using var harness = SignInHarness.Create(database.ConnectionString);

        var userId = await harness.SeedUserAsync(
            $"seed|{Guid.CreateVersion7()}",
            $"{Guid.CreateVersion7():n}@fake.local",
            isActive: false);

        var result = await harness.WithServicesAsync(services =>
            services.GetRequiredService<DeactivateUser>().ExecuteAsync(userId));

        // Null rather than zero: nothing happened, so nothing should be audited as having happened.
        result.ShouldBeNull();
    }

    /// <summary>
    /// A second session for the same user, started through the store as a second device would.
    /// </summary>
    private static async Task<IssuedTestCredential> IssueSecondSessionAsync(SignInHarness harness, Guid userId) =>
        await harness.WithServicesAsync(async services =>
        {
            var sessions = services.GetRequiredService<Crm.Application.Abstractions.ISessionStore>();
            var issuer = services.GetRequiredService<Crm.Application.Abstractions.ITokenIssuer>();
            var store = services.GetRequiredService<Crm.Application.Abstractions.IIdentityStore>();

            var user = await store.FindByIdAsync(userId);
            user.ShouldNotBeNull();

            var session = await sessions.StartAsync(userId, "second-device", "127.0.0.1");
            var permissions = await store.GetEffectivePermissionsAsync(userId);

            var credential = issuer.Issue(new Crm.Application.Abstractions.IssuedIdentity(
                userId,
                session.SessionId,
                user.DisplayName,
                user.Email,
                Crm.Application.Abstractions.CallerPopulation.Staff,
                permissions,
                user.Scope));

            return new IssuedTestCredential(
                credential.Value,
                userId,
                session.SessionId,
                session.RenewalCredential);
        });

    private static Uri Diagnostics => new("/api/v1/diagnostics/items", UriKind.Relative);

    private static Uri SignOut => new("/api/v1/auth/sign-out", UriKind.Relative);
}
