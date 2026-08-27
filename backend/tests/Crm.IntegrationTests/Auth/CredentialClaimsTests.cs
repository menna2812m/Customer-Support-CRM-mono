using Crm.Application.Authorization;
using Crm.IntegrationTests.Infrastructure;
using Microsoft.IdentityModel.JsonWebTokens;
using Shouldly;

namespace Crm.IntegrationTests.Auth;

/// <summary>
/// The claim names in an issued credential are a contract between the issuer (Infrastructure) and
/// the reader (the API's CurrentUser). They live in two files that may not reference each other, so
/// a test asserts they agree rather than trusting that they do.
/// </summary>
[Collection(DatabaseCollectionDefinition.Name)]
public sealed class CredentialClaimsTests(SqlServerFixture database)
{
    [Fact]
    public async Task An_issued_credential_carries_the_claims_the_api_reads()
    {
        await using var factory = new CrmWebApplicationFactory(database.ConnectionString);
        var issued = await TestTokens.IssueStaffAsync(factory.Services, Permissions.Diagnostics.Read);

        var token = new JsonWebToken(issued.AccessCredential);
        var claims = token.Claims.Select(claim => $"{claim.Type}={claim.Value}").ToList();

        claims.ShouldContain($"crm_session={issued.SessionId}", customMessage: string.Join(" | ", claims));
        claims.ShouldContain($"permission={Permissions.Diagnostics.Read}", customMessage: string.Join(" | ", claims));
        claims.ShouldContain("crm_population=Staff", customMessage: string.Join(" | ", claims));
    }
}
