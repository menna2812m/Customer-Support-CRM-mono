using System.Net;
using System.Text.Json;
using Crm.Domain.Identity;
using Crm.IntegrationTests.Infrastructure;
using Shouldly;

namespace Crm.IntegrationTests.Auth;

/// <summary>
/// User Story 2: a session lasts a working day, renewal is invisible, and the credential that
/// carries it is single-use (spec FR-011 to FR-014, FR-017).
///
/// The renewal credential is the one long-lived secret in the system, so the properties tested
/// here are the ones that keep it from being worth stealing: it changes on every use, using a
/// spent one ends the session outright, and neither limit can be extended past its bound.
/// </summary>
[Collection(DatabaseCollectionDefinition.Name)]
public sealed class SessionRenewalTests(SqlServerFixture database)
{
    [Fact]
    public async Task Renewal_issues_a_new_credential_and_rotates_the_cookie()
    {
        await using var signedIn = await SignedInAsync();
        var harness = signedIn.Harness;

        var first = harness.RenewalCookie;
        first.ShouldNotBeNull();

        var credential = await harness.IssueAccessCredentialAsync();

        credential.ShouldNotBeNullOrWhiteSpace();

        // The response set a new cookie, which the jar has already picked up.
        var second = harness.RenewalCookie;

        second.ShouldNotBeNull();
        second.ShouldNotBe(first, "each renewal must replace the credential it spent");

        // Note what is *not* asserted: that two renewals produce different access credentials.
        // They do not, and need not - a credential is a signed statement about a session at a
        // second, so two issued in the same second are byte-identical and equally valid. Rotation
        // is a property of the renewal credential, which is the one an attacker would want.
        //
        // The credential from before the rotation is spent. Presenting it is not merely refused -
        // see the reuse test below for what it costs.
        using var replay = await harness.RequestSessionAsync(first);
        replay.StatusCode.ShouldBe(HttpStatusCode.Unauthorized);
    }

    [Fact]
    public async Task Presenting_a_spent_credential_revokes_the_session_and_the_newest_one_stops_working_too()
    {
        await using var signedIn = await SignedInAsync();
        var harness = signedIn.Harness;

        var spent = harness.RenewalCookie!;

        await harness.IssueAccessCredentialAsync();

        var newest = harness.RenewalCookie!;
        newest.ShouldNotBe(spent);

        // Two parties now hold credentials from the same chain, which can only mean one of them
        // copied it. The CRM cannot tell which is the legitimate client, so it ends the session.
        using var replay = await harness.RequestSessionAsync(spent);
        replay.StatusCode.ShouldBe(HttpStatusCode.Unauthorized);

        // The decisive assertion: the *legitimate* holder is signed out as well. A design that
        // merely refused the replay would leave a thief's copy working alongside the real one.
        using var afterReuse = await harness.RequestSessionAsync(newest);
        afterReuse.StatusCode.ShouldBe(HttpStatusCode.Unauthorized);
    }

    [Fact]
    public async Task Reuse_is_recorded_as_reuse_rather_than_as_an_ordinary_expiry()
    {
        await using var signedIn = await SignedInAsync();
        var harness = signedIn.Harness;

        var spent = harness.RenewalCookie!;
        await harness.IssueAccessCredentialAsync();

        using var replay = await harness.RequestSessionAsync(spent);
        replay.StatusCode.ShouldBe(HttpStatusCode.Unauthorized);

        var sessions = await harness.GetSessionsForUserAsync(signedIn.UserId);

        sessions.ShouldContain(session => session.RevokedReason == SessionRevocationReason.CredentialReused);

        var events = await harness.GetEventsForUserAsync(signedIn.UserId);

        events.ShouldContain(entry => entry.Action == AuthenticationActions.CredentialReused);
    }

    [Fact]
    public async Task Renewal_is_refused_once_the_inactivity_limit_has_passed()
    {
        await using var signedIn = await SignedInAsync();
        var harness = signedIn.Harness;

        // The default inactivity limit is eight hours. Nine hours of doing nothing is a session
        // that must not come back to life on the tenth.
        harness.Clock.Advance(TimeSpan.FromHours(9));

        using var response = await harness.RequestSessionAsync();

        response.StatusCode.ShouldBe(HttpStatusCode.Unauthorized);
        (await ReadCodeAsync(response)).ShouldBe("session_expired");
    }

    [Fact]
    public async Task Renewal_is_refused_once_the_absolute_lifetime_has_passed_however_active_the_user_was()
    {
        await using var signedIn = await SignedInAsync();
        var harness = signedIn.Harness;

        // Twelve hours from sign-in, whatever happened in between. Renewing every four hours keeps
        // the session inside its inactivity window the whole time, which is exactly the case the
        // absolute limit exists to bound.
        harness.Clock.Advance(TimeSpan.FromHours(4));
        (await harness.RequestSessionAsync()).StatusCode.ShouldBe(HttpStatusCode.OK);

        harness.Clock.Advance(TimeSpan.FromHours(4));
        (await harness.RequestSessionAsync()).StatusCode.ShouldBe(HttpStatusCode.OK);

        harness.Clock.Advance(TimeSpan.FromHours(5));

        using var response = await harness.RequestSessionAsync();

        response.StatusCode.ShouldBe(HttpStatusCode.Unauthorized);
        (await ReadCodeAsync(response)).ShouldBe("session_expired");
    }

    [Fact]
    public async Task Renewal_refuses_a_request_without_the_application_header()
    {
        await using var signedIn = await SignedInAsync();

        // A cross-site form post carries the cookie but cannot set a custom header.
        using var response = await signedIn.Harness.RequestSessionAsync(withApplicationHeader: false);

        response.StatusCode.ShouldBe(HttpStatusCode.Forbidden);
    }

    [Fact]
    public async Task Renewal_refuses_a_request_from_an_origin_that_is_not_allow_listed()
    {
        await using var signedIn = await SignedInAsync();
        var harness = signedIn.Harness;

        // The header alone is not enough: a cross-origin script can set one, and CORS would only
        // stop the caller from reading the response - after the session had already rotated.
        using var refused = await harness.RequestSessionAsync(origin: "https://evil.example");

        refused.StatusCode.ShouldBe(HttpStatusCode.Forbidden);

        // The allow-listed origin still works, so the check is not simply refusing everything.
        using var allowed = await harness.RequestSessionAsync(origin: CrmWebApplicationFactory.TestOrigin);

        allowed.StatusCode.ShouldBe(HttpStatusCode.OK);
    }

    /// <summary>
    /// A harness with one signed-in administrator. Bootstrap rather than the default role, because
    /// these tests need a session rather than a particular set of permissions.
    /// </summary>
    private async Task<SignedInFixture> SignedInAsync()
    {
        var email = $"{Guid.CreateVersion7():n}@fake.local";

        var harness = SignInHarness.Create(
            database.ConnectionString,
            new Dictionary<string, string?> { ["Identity:BootstrapAdministrator"] = email });

        var account = harness.Provider.AddAccount(email: email);

        (await harness.SignInAsync(account)).Succeeded.ShouldBeTrue();

        return new SignedInFixture(harness, await harness.GetUserIdAsync(account.Subject));
    }

    private static async Task<string?> ReadCodeAsync(HttpResponseMessage response)
    {
        using var document = JsonDocument.Parse(await response.Content.ReadAsStringAsync());

        return document.RootElement.TryGetProperty("code", out var code) ? code.GetString() : null;
    }

    private sealed record SignedInFixture(SignInHarness Harness, Guid UserId) : IAsyncDisposable
    {
        public ValueTask DisposeAsync() => Harness.DisposeAsync();
    }
}
