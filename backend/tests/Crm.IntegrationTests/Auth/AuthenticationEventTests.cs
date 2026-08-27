using System.Net.Http.Json;
using Crm.Domain.Identity;
using Crm.IntegrationTests.Infrastructure;
using Shouldly;

namespace Crm.IntegrationTests.Auth;

/// <summary>
/// User Story 4, second half: every authentication decision is answerable afterwards (spec FR-039).
///
/// The test that matters most here is the last one. An audit trail that records the credential it
/// was auditing would turn the safest table in the database into the most dangerous, and the
/// mistake is easy to make - a "detail" field is exactly where a token ends up when somebody is
/// debugging.
/// </summary>
[Collection(DatabaseCollectionDefinition.Name)]
public sealed class AuthenticationEventTests(SqlServerFixture database)
{
    [Fact]
    public async Task A_sign_in_a_renewal_and_a_sign_out_each_leave_a_record()
    {
        var email = $"{Guid.CreateVersion7():n}@fake.local";

        await using var harness = SignInHarness.Create(
            database.ConnectionString,
            new Dictionary<string, string?> { ["Identity:BootstrapAdministrator"] = email });

        var account = harness.Provider.AddAccount(email: email);
        (await harness.SignInAsync(account)).Succeeded.ShouldBeTrue();

        var userId = await harness.GetUserIdAsync(account.Subject);

        var credential = await harness.IssueAccessCredentialAsync();
        using var client = harness.CreateAuthenticatedClient(credential);

        await client.PostAsJsonAsync(
            new Uri("/api/v1/auth/sign-out", UriKind.Relative),
            new { allSessions = false, endProviderSession = false });

        var events = await harness.GetEventsForUserAsync(userId);
        var actions = events.Select(entry => entry.Action).ToList();

        actions.ShouldContain(AuthenticationActions.SignInSucceeded);
        actions.ShouldContain(AuthenticationActions.RoleGranted);
        actions.ShouldContain(AuthenticationActions.SessionRenewed);
        actions.ShouldContain(AuthenticationActions.SessionRevoked);

        // An event nobody can trace back to a request is an event nobody can investigate.
        events.ShouldAllBe(entry => entry.CorrelationId != string.Empty);
        events.ShouldAllBe(entry => entry.Outcome != string.Empty);
    }

    [Fact]
    public async Task A_refusal_is_recorded_even_though_there_is_no_session_to_trace()
    {
        await using var harness = SignInHarness.Create(
            database.ConnectionString,

            // No default role and no bootstrap administrator: the user is recognised and granted
            // nothing, which is a refusal rather than a failure.
            new Dictionary<string, string?> { ["Identity:DefaultRole"] = null });

        var account = harness.Provider.AddAccount();

        var result = await harness.SignInAsync(account);
        result.Error.ShouldBe("no_access");

        var events = await harness.GetEventsAsync(account.Subject);

        events.ShouldContain(entry =>
            entry.Action == AuthenticationActions.SignInRefused
            && entry.Outcome == AuthenticationOutcomes.Refused);

        // The refusal names the subject that was presented, so an operator can answer "why can this
        // person not sign in" without a session identifier to search on.
        events.ShouldContain(entry => entry.SubjectReference == account.Subject);
    }

    [Fact]
    public async Task No_authentication_event_carries_a_token_a_cookie_value_or_a_credential_hash()
    {
        var email = $"{Guid.CreateVersion7():n}@fake.local";

        await using var harness = SignInHarness.Create(
            database.ConnectionString,
            new Dictionary<string, string?> { ["Identity:BootstrapAdministrator"] = email });

        var account = harness.Provider.AddAccount(email: email);
        (await harness.SignInAsync(account)).Succeeded.ShouldBeTrue();

        var renewalCookie = harness.RenewalCookie!;
        var userId = await harness.GetUserIdAsync(account.Subject);

        var accessCredential = await harness.IssueAccessCredentialAsync();
        var rotatedCookie = harness.RenewalCookie!;

        using var client = harness.CreateAuthenticatedClient(accessCredential);

        await client.PostAsJsonAsync(
            new Uri("/api/v1/auth/sign-out", UriKind.Relative),
            new { allSessions = false, endProviderSession = false });

        var events = await harness.GetEventsForUserAsync(userId);

        events.ShouldNotBeEmpty("the cycle above must have produced something to inspect");

        var secrets = new[]
        {
            renewalCookie,
            rotatedCookie,
            accessCredential,

            // The stored hash is not a secret in the same sense, but it is the value the store
            // compares against - putting it in an audit row would make the audit trail a lookup
            // table for live sessions.
            HashOf(renewalCookie),
            HashOf(rotatedCookie),
        };

        foreach (var entry in events)
        {
            var written = string.Join(
                '|',
                entry.Action,
                entry.Outcome,
                entry.Detail,
                entry.SubjectReference,
                entry.CorrelationId,
                entry.IpAddress);

            foreach (var secret in secrets)
            {
                written.ShouldNotContain(secret);
            }
        }
    }

    private static string HashOf(string value) =>
        Convert.ToBase64String(
            System.Security.Cryptography.SHA256.HashData(System.Text.Encoding.UTF8.GetBytes(value)));
}
