using Crm.Application.Common;
using Crm.Domain.Organization;

namespace Crm.Application.Abstractions;

/// <summary>A unit as it leaves the store. Never an entity - Constitution III.</summary>
public sealed record OrganizationUnitRecord(
    Guid Id,
    string NameAr,
    string NameEn,
    string Code,
    bool IsActive);

/// <summary>
/// A team carries its department, so a placement chooser can show "Technical Support / Tier 1"
/// without a second call. Feature 004 depends on this shape.
/// </summary>
public sealed record TeamRecord(
    Guid Id,
    string NameAr,
    string NameEn,
    string Code,
    bool IsActive,
    Guid DepartmentId,
    string DepartmentNameAr,
    string DepartmentNameEn);

/// <summary>
/// What a list endpoint asks for. <paramref name="ActiveOnly"/> exists so that a consumer choosing
/// a placement never has to filter inactive units out for itself (spec FR-009).
/// </summary>
public sealed record UnitListQuery(PageRequest Paging, string? Search, bool ActiveOnly, string Language);

/// <summary>
/// What still depends on a unit. A delete refusal names these, because a refusal that does not say
/// why cannot be acted on (spec FR-012).
/// </summary>
public sealed record DependentSummary(int Teams, int People)
{
    public bool Any => Teams > 0 || People > 0;
}

/// <summary>The outcome of a team move, including how many people it carried with it (AR-006).</summary>
public sealed record TeamMoveResult(TeamRecord Team, int MembersReassigned);

/// <summary>
/// Reading and writing organizational structure. Generic over the shared shape wherever branches and
/// departments behave identically, and explicit wherever teams do not - which is exactly the
/// containment rule, and nowhere else.
/// </summary>
public interface IOrganizationStore
{
    Task<PagedResult<OrganizationUnitRecord>> ListAsync<TUnit>(
        UnitListQuery query,
        CancellationToken cancellationToken = default)
        where TUnit : OrganizationUnit;

    Task<OrganizationUnitRecord?> FindAsync<TUnit>(Guid id, CancellationToken cancellationToken = default)
        where TUnit : OrganizationUnit;

    /// <summary>True when a live unit of this kind already holds the code (spec FR-006).</summary>
    Task<bool> CodeExistsAsync<TUnit>(string code, CancellationToken cancellationToken = default)
        where TUnit : OrganizationUnit;

    /// <summary>
    /// True when a live unit of this kind, other than <paramref name="excluding"/>, already holds
    /// either name. Each language is checked independently (spec FR-005).
    /// </summary>
    Task<bool> NameExistsAsync<TUnit>(
        string nameAr,
        string nameEn,
        Guid? excluding = null,
        CancellationToken cancellationToken = default)
        where TUnit : OrganizationUnit;

    Task<OrganizationUnitRecord> AddAsync<TUnit>(TUnit unit, CancellationToken cancellationToken = default)
        where TUnit : OrganizationUnit;

    Task<OrganizationUnitRecord?> RenameAsync<TUnit>(
        Guid id,
        string nameAr,
        string nameEn,
        CancellationToken cancellationToken = default)
        where TUnit : OrganizationUnit;

    Task<OrganizationUnitRecord?> SetActivationAsync<TUnit>(
        Guid id,
        bool isActive,
        CancellationToken cancellationToken = default)
        where TUnit : OrganizationUnit;

    /// <summary>Soft-deletes the unit. The caller checks dependents first (spec FR-012).</summary>
    Task<bool> DeleteAsync<TUnit>(Guid id, CancellationToken cancellationToken = default)
        where TUnit : OrganizationUnit;

    Task<DependentSummary> CountDependentsAsync<TUnit>(Guid id, CancellationToken cancellationToken = default)
        where TUnit : OrganizationUnit;

    Task<PagedResult<TeamRecord>> ListTeamsAsync(
        Guid departmentId,
        UnitListQuery query,
        CancellationToken cancellationToken = default);

    Task<TeamRecord?> FindTeamAsync(Guid id, CancellationToken cancellationToken = default);

    /// <summary>True when the department already has a live team of either name (spec FR-005).</summary>
    Task<bool> TeamNameExistsInDepartmentAsync(
        Guid departmentId,
        string nameAr,
        string nameEn,
        Guid? excluding = null,
        CancellationToken cancellationToken = default);

    /// <summary>Creates a team in a department. Null when no such department exists.</summary>
    Task<TeamRecord?> CreateTeamAsync(
        Guid departmentId,
        string nameAr,
        string nameEn,
        string code,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Moves a team and reassigns every member's recorded department, as one atomic operation
    /// (spec FR-015). Null when the team or the destination does not exist.
    /// </summary>
    Task<TeamMoveResult?> MoveTeamAsync(
        Guid teamId,
        Guid destinationDepartmentId,
        CancellationToken cancellationToken = default);
}
