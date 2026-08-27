using System.Security.Claims;
using Crm.Application.Abstractions;

namespace Crm.Api.Common.Security;

/// <summary>
/// Resolves the acting caller from the authenticated principal (spec FR-027).
///
/// The population comes from the claim stamped by the authenticating scheme, never from a value
/// the caller supplied - otherwise a portal token could claim to be staff. A portal caller has no
/// organizational scope, and nothing here assumes one.
/// </summary>
public sealed class CurrentUser(IHttpContextAccessor accessor) : ICurrentUser
{
    private ClaimsPrincipal? Principal => accessor.HttpContext?.User;

    public bool IsAuthenticated => Principal?.Identity?.IsAuthenticated == true;

    public Guid? UserId =>
        Guid.TryParse(
            Principal?.FindFirstValue(ClaimTypes.NameIdentifier) ?? Principal?.FindFirstValue("sub"),
            out var id)
            ? id
            : null;

    public CallerPopulation? Population =>
        Enum.TryParse<CallerPopulation>(Principal?.FindFirstValue(CrmClaims.Population), out var population)
            ? population
            : null;

    public IReadOnlySet<string> Permissions =>
        Principal is null
            ? new HashSet<string>(StringComparer.Ordinal)
            : Principal
                .FindAll(CrmClaims.Permission)
                .Select(claim => claim.Value)
                .ToHashSet(StringComparer.Ordinal);

    public OrganizationScope? Scope
    {
        get
        {
            // Portal callers are external customers: they have no place in the organization chart,
            // and code must not invent one for them.
            if (Population != CallerPopulation.Staff)
            {
                return null;
            }

            return new OrganizationScope(
                ParseGuid(CrmClaims.DepartmentId),
                ParseGuid(CrmClaims.BranchId),
                ParseGuid(CrmClaims.TeamId));
        }
    }

    private Guid? ParseGuid(string claimType) =>
        Guid.TryParse(Principal?.FindFirstValue(claimType), out var value) ? value : null;
}
