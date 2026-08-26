namespace Crm.Application.Abstractions;

/// <summary>
/// Which population a caller belongs to. Resolved from the authenticating scheme, never from a
/// client-supplied claim (spec FR-023, FR-027).
/// </summary>
public enum CallerPopulation
{
    /// <summary>Agents, supervisors, administrators - federated from the corporate identity provider.</summary>
    Staff = 1,

    /// <summary>External customer portal users - CRM-owned accounts.</summary>
    Portal = 2,
}

/// <summary>
/// Organizational placement of a caller. Constitution V: no code may assume a single department
/// or a single branch. A portal caller has no organizational scope at all.
/// </summary>
public sealed record OrganizationScope(Guid? DepartmentId, Guid? BranchId, Guid? TeamId);

/// <summary>
/// The acting caller, as the Application layer sees it. Implemented over HTTP claims in the API
/// layer and substituted directly in tests, so no handler depends on HTTP.
/// </summary>
public interface ICurrentUser
{
    bool IsAuthenticated { get; }

    Guid? UserId { get; }

    CallerPopulation? Population { get; }

    /// <summary>Permission names held by the caller. Empty when unauthenticated.</summary>
    IReadOnlySet<string> Permissions { get; }

    /// <summary>Null for portal callers by design, and for anonymous requests.</summary>
    OrganizationScope? Scope { get; }
}
