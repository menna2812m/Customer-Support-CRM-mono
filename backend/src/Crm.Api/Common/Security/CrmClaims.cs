namespace Crm.Api.Common.Security;

/// <summary>
/// Claim types the CRM reads. Population is never taken from the token itself - it is stamped by
/// the authenticating scheme, so a caller cannot promote itself by crafting a claim
/// (spec FR-023, FR-027).
/// </summary>
public static class CrmClaims
{
    /// <summary>Permission held by the caller. Repeated once per permission.</summary>
    public const string Permission = "permission";

    /// <summary>Stamped by the authentication scheme, not read from the incoming token.</summary>
    public const string Population = "crm_population";

    public const string DepartmentId = "crm_department";
    public const string BranchId = "crm_branch";
    public const string TeamId = "crm_team";
}
