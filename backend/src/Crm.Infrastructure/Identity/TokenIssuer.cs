using System.Security.Claims;
using System.Text;
using Crm.Application.Abstractions;
using Microsoft.Extensions.Options;
using Microsoft.IdentityModel.JsonWebTokens;
using Microsoft.IdentityModel.Tokens;

namespace Crm.Infrastructure.Identity;

/// <summary>
/// Issues the CRM's own access credentials (research decision 1, 4).
///
/// Symmetric signing is deliberate while the CRM is the only issuer and the only validator: there
/// is no public key to distribute. The key identifier in the header costs nothing now and is what
/// allows a key to be rotated later without invalidating every live session.
/// </summary>
public sealed class TokenIssuer(IOptions<TokenIssuerSettings> settings, TimeProvider clock) : ITokenIssuer
{
    public AccessCredential Issue(IssuedIdentity identity)
    {
        ArgumentNullException.ThrowIfNull(identity);

        var options = settings.Value;

        if (string.IsNullOrWhiteSpace(options.SigningKey))
        {
            throw new InvalidOperationException(
                "No signing key is configured, so no access credential can be issued. "
                    + "Supply Token:SigningKey through the secrets source.");
        }

        var now = clock.GetUtcNow();
        var expiresAt = now.AddMinutes(options.AccessCredentialMinutes);

        var claims = new List<Claim>
        {
            new(ClaimTypes.NameIdentifier, identity.UserId.ToString()),
            new(CrmClaimNames.SessionId, identity.SessionId.ToString()),
            new(CrmClaimNames.Population, identity.Population.ToString()),
            new("name", identity.DisplayName),
            new("email", identity.Email),
        };

        // One claim per permission: the authorization handler reads them as a set, and a caller
        // cannot add to them because this credential is signed here.
        claims.AddRange(identity.Permissions.Select(permission => new Claim(CrmClaimNames.Permission, permission)));

        if (identity.Scope is { } scope)
        {
            AddIfPresent(claims, CrmClaimNames.DepartmentId, scope.DepartmentId);
            AddIfPresent(claims, CrmClaimNames.BranchId, scope.BranchId);
            AddIfPresent(claims, CrmClaimNames.TeamId, scope.TeamId);
        }

        var key = new SymmetricSecurityKey(Encoding.UTF8.GetBytes(options.SigningKey)) { KeyId = options.KeyId };

        var descriptor = new SecurityTokenDescriptor
        {
            Issuer = options.Issuer,
            Audience = options.Audience,
            Subject = new ClaimsIdentity(claims, "CrmBearer"),
            IssuedAt = now.UtcDateTime,
            NotBefore = now.UtcDateTime,
            Expires = expiresAt.UtcDateTime,
            SigningCredentials = new SigningCredentials(key, SecurityAlgorithms.HmacSha256),
        };

        var value = new JsonWebTokenHandler().CreateToken(descriptor);

        return new AccessCredential(value, expiresAt, options.AccessCredentialMinutes * 60);
    }

    private static void AddIfPresent(List<Claim> claims, string type, Guid? value)
    {
        if (value is { } id)
        {
            claims.Add(new Claim(type, id.ToString()));
        }
    }
}

/// <summary>
/// Issuance settings, mirrored into Infrastructure so the API layer's options type does not have to
/// be referenced here - the architecture test forbids that direction.
/// </summary>
public sealed class TokenIssuerSettings
{
    public string Issuer { get; init; } = string.Empty;

    public string Audience { get; init; } = string.Empty;

    public string? SigningKey { get; init; }

    public string KeyId { get; init; } = "k1";

    public int AccessCredentialMinutes { get; init; } = 15;
}

/// <summary>
/// Claim names, duplicated from the API layer's <c>CrmClaims</c> because Infrastructure may not
/// reference it. They are asserted equal by a test, so the two cannot drift apart silently.
/// </summary>
public static class CrmClaimNames
{
    public const string Permission = "permission";
    public const string Population = "crm_population";
    public const string DepartmentId = "crm_department";
    public const string BranchId = "crm_branch";
    public const string TeamId = "crm_team";
    public const string SessionId = "crm_session";
}
