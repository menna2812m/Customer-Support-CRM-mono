using Crm.Application.Abstractions;
using Crm.Domain.Identity;
using Crm.Infrastructure.Persistence;
using Microsoft.Extensions.DependencyInjection;

namespace Crm.IntegrationTests.Infrastructure;

/// <summary>
/// Issues credentials for tests using the application's own issuer and a real session row.
///
/// Feature 001 minted standalone tokens, which was correct while the CRM validated tokens from an
/// identity provider. Feature 002 made the CRM the issuer and made revocation immediate, so a
/// credential is only meaningful alongside a live session - the scheme checks on every request.
/// Seeding a real user and session keeps the tests exercising that check rather than stepping
/// around it.
/// </summary>
public static class TestTokens
{
    public const string Issuer = "https://crm.tests.local";
    public const string Audience = "crm-api";
    public const string SigningKey = "integration-test-signing-key-that-is-long-enough-for-hmac-sha256";

    /// <summary>Configuration that makes the application issue and validate the credentials below.</summary>
    public static Dictionary<string, string?> TokenConfiguration() => new()
    {
        ["Token:Issuer"] = Issuer,
        ["Token:Audience"] = Audience,
        ["Token:SigningKey"] = SigningKey,
        ["Token:KeyId"] = "test",
        ["Token:AccessCredentialMinutes"] = "15",
    };

    /// <summary>
    /// Creates a staff user with the given permissions, starts a session, and returns a credential
    /// for it. The permissions are attached directly rather than through a role, so a test about
    /// authorization does not also depend on role seeding.
    /// </summary>
    public static async Task<IssuedTestCredential> IssueStaffAsync(
        IServiceProvider services,
        params string[] permissions)
    {
        ArgumentNullException.ThrowIfNull(services);
        ArgumentNullException.ThrowIfNull(permissions);

        using var scope = services.CreateScope();
        var context = scope.ServiceProvider.GetRequiredService<CrmDbContext>();
        var sessions = scope.ServiceProvider.GetRequiredService<ISessionStore>();
        var issuer = scope.ServiceProvider.GetRequiredService<ITokenIssuer>();

        var user = User.Provision(
            provider: "https://tests.local/realms/crm",
            providerSubject: $"test|{Guid.CreateVersion7()}",
            email: $"{Guid.CreateVersion7():n}@tests.local",
            displayName: "Test Staff",
            population: (int)CallerPopulation.Staff,
            placement: OrganizationPlacement.None);

        context.Users.Add(user);
        await context.SaveChangesAsync();

        var session = await sessions.StartAsync(user.Id, "integration-tests", "127.0.0.1");

        var credential = issuer.Issue(new IssuedIdentity(
            user.Id,
            session.SessionId,
            user.DisplayName,
            user.Email,
            CallerPopulation.Staff,
            permissions.ToHashSet(StringComparer.Ordinal),
            Scope: null));

        return new IssuedTestCredential(credential.Value, user.Id, session.SessionId, session.RenewalCredential);
    }

    /// <summary>
    /// A credential for the portal population. The portal feature is deferred, so this exists only
    /// to prove that a staff-only endpoint refuses a portal caller (spec AR-004).
    /// </summary>
    public static async Task<IssuedTestCredential> IssuePortalAsync(
        IServiceProvider services,
        params string[] permissions)
    {
        ArgumentNullException.ThrowIfNull(services);

        using var scope = services.CreateScope();
        var context = scope.ServiceProvider.GetRequiredService<CrmDbContext>();
        var sessions = scope.ServiceProvider.GetRequiredService<ISessionStore>();
        var issuer = scope.ServiceProvider.GetRequiredService<ITokenIssuer>();

        var user = User.Provision(
            provider: "https://tests.local/realms/crm",
            providerSubject: $"portal|{Guid.CreateVersion7()}",
            email: $"{Guid.CreateVersion7():n}@customers.local",
            displayName: "Test Customer",
            population: (int)CallerPopulation.Portal,
            placement: OrganizationPlacement.None);

        context.Users.Add(user);
        await context.SaveChangesAsync();

        var session = await sessions.StartAsync(user.Id, "integration-tests", "127.0.0.1");

        var credential = issuer.Issue(new IssuedIdentity(
            user.Id,
            session.SessionId,
            user.DisplayName,
            user.Email,
            CallerPopulation.Portal,
            (permissions ?? []).ToHashSet(StringComparer.Ordinal),
            Scope: null));

        return new IssuedTestCredential(credential.Value, user.Id, session.SessionId, session.RenewalCredential);
    }
}

/// <param name="AccessCredential">Bearer value for the Authorization header.</param>
/// <param name="UserId">The seeded user, for tests that need to change or deactivate it.</param>
/// <param name="SessionId">The live session, for tests that revoke it.</param>
/// <param name="RenewalCredential">The renewal value, for tests that rotate or replay it.</param>
public sealed record IssuedTestCredential(
    string AccessCredential,
    Guid UserId,
    Guid SessionId,
    string RenewalCredential);
