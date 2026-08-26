using System.Security.Claims;
using System.Text;
using Crm.Api.Common.Security;
using Microsoft.IdentityModel.JsonWebTokens;
using Microsoft.IdentityModel.Tokens;

namespace Crm.IntegrationTests.Infrastructure;

/// <summary>
/// Issues locally signed tokens for the two populations so authorization can be tested without an
/// identity provider. The application validates these exactly as it would real ones - same
/// schemes, same claims, same pipeline.
/// </summary>
public static class TestTokens
{
    public const string SigningKey = "integration-test-signing-key-that-is-long-enough-for-hmac-sha256";
    public const string StaffIssuer = "https://test.identity.local/staff";
    public const string PortalIssuer = "https://test.crm.local/portal";
    public const string Audience = "crm-api";

    /// <summary>Configuration that switches both schemes on with the local test keys.</summary>
    public static Dictionary<string, string?> AuthConfiguration() => new()
    {
        ["Authentication:Staff:Enabled"] = "true",
        ["Authentication:Staff:Issuer"] = StaffIssuer,
        ["Authentication:Staff:Audience"] = Audience,
        ["Authentication:Staff:SigningKey"] = SigningKey,
        ["Authentication:Portal:Enabled"] = "true",
        ["Authentication:Portal:Issuer"] = PortalIssuer,
        ["Authentication:Portal:Audience"] = Audience,
        ["Authentication:Portal:SigningKey"] = SigningKey,
    };

    public static string Staff(params string[] permissions) =>
        Create(StaffIssuer, Guid.CreateVersion7(), permissions);

    public static string Portal(params string[] permissions) =>
        Create(PortalIssuer, Guid.CreateVersion7(), permissions);

    private static string Create(string issuer, Guid userId, IEnumerable<string> permissions)
    {
        var claims = new List<Claim>
        {
            new(ClaimTypes.NameIdentifier, userId.ToString()),
            new(CrmClaims.DepartmentId, Guid.CreateVersion7().ToString()),
        };

        claims.AddRange(permissions.Select(permission => new Claim(CrmClaims.Permission, permission)));

        // Deliberately NOT setting the population claim: the scheme stamps it, and a token that
        // tries to set it must not be able to promote itself.
        var descriptor = new SecurityTokenDescriptor
        {
            Issuer = issuer,
            Audience = Audience,
            Subject = new ClaimsIdentity(claims, "Test"),
            Expires = DateTime.UtcNow.AddMinutes(10),
            SigningCredentials = new SigningCredentials(
                new SymmetricSecurityKey(Encoding.UTF8.GetBytes(SigningKey)),
                SecurityAlgorithms.HmacSha256),
        };

        return new JsonWebTokenHandler().CreateToken(descriptor);
    }
}
