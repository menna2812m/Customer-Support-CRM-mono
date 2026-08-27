namespace Crm.Application.Abstractions;

/// <summary>
/// Issues the access credential the API validates (research decision 1).
///
/// The CRM is the issuer, not the identity provider: only a credential we mint can be revoked on
/// sign-out, rotated single-use, and carry CRM permissions.
/// </summary>
public interface ITokenIssuer
{
    AccessCredential Issue(IssuedIdentity identity);
}

/// <param name="Value">The signed credential. Held in memory by the client, never stored.</param>
public sealed record AccessCredential(string Value, DateTimeOffset ExpiresAt, int ExpiresInSeconds);

/// <summary>
/// Everything the credential asserts. Assembled by the application from the CRM's own records -
/// nothing here is copied from an inbound provider token (spec FR-027).
/// </summary>
public sealed record IssuedIdentity(
    Guid UserId,
    Guid SessionId,
    string DisplayName,
    string Email,
    CallerPopulation Population,
    IReadOnlySet<string> Permissions,
    OrganizationScope? Scope);
