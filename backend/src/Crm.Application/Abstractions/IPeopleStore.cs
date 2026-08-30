using Crm.Application.Common;
using Crm.Application.Identity.People;

namespace Crm.Application.Abstractions;

/// <summary>
/// Reading and changing people, for the administration surface (feature 004).
/// </summary>
/// <remarks>
/// Separate from <c>IIdentityStore</c>, which serves sign-in. The two touch the same table and
/// answer different questions: sign-in asks "who is this arriving identity", administration asks
/// "who exists, and what may they do". Merging them would give every sign-in a dependency on the
/// paged reads and filters that only an administrator needs.
///
/// Every write that could reduce administrator access evaluates <see cref="AdministratorGuard"/>
/// and performs the change inside one serializable transaction. The isolation is the implementation
/// detail that makes the guard's answer still true when it is acted on, which is why the guard is
/// not exposed here as something a caller could check separately and then act on later.
/// </remarks>
public interface IPeopleStore
{
    /// <summary>One page of people, filtered as the administration list offers.</summary>
    Task<PagedResult<PersonSummary>> ListAsync(
        PeopleQuery query,
        CancellationToken cancellationToken = default);

    /// <summary>One person with their roles and effective permissions, or null.</summary>
    Task<PersonDetail?> FindAsync(Guid personId, CancellationToken cancellationToken = default);

    /// <summary>
    /// Creates somebody who has never signed in. Refused when the address already belongs to a
    /// person who is not deleted (spec FR-014).
    /// </summary>
    Task<PersonWriteResult> PreProvisionAsync(
        PreProvisionCommand command,
        CancellationToken cancellationToken = default);

    /// <summary>Grants a role. Granting one already held succeeds and creates no duplicate (FR-008).</summary>
    Task<PersonWriteResult> GrantRoleAsync(
        Guid actorId,
        Guid personId,
        Guid roleId,
        CancellationToken cancellationToken = default);

    /// <summary>Revokes a role, guarded against leaving no administrator and against self-demotion.</summary>
    Task<PersonWriteResult> RevokeRoleAsync(
        Guid actorId,
        Guid personId,
        Guid roleId,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Sets branch, department, and team together, deriving the department from the team when one
    /// is given and refusing a department that disagrees (spec FR-010, FR-011).
    /// </summary>
    Task<PersonWriteResult> SetPlacementAsync(
        Guid actorId,
        Guid personId,
        PlacementCommand placement,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Activates or deactivates. Deactivating ends every active session at once, because access
    /// that ends at the next renewal has not ended (spec FR-023).
    /// </summary>
    Task<PersonWriteResult> SetActivationAsync(
        Guid actorId,
        Guid personId,
        bool isActive,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Deletes as one indivisible operation: revoke every role, end every session, soft-delete the
    /// person, and report the roles held immediately beforehand so the caller can record them.
    /// </summary>
    /// <remarks>
    /// The roles are returned rather than merely revoked because <c>RoleAssignment</c> has no
    /// revocation history - revoking deletes the only row that recorded the grant. If the audit
    /// entry does not carry them, that history exists nowhere (spec FR-025).
    /// </remarks>
    Task<PersonDeletionResult> DeleteAsync(
        Guid actorId,
        Guid personId,
        CancellationToken cancellationToken = default);
}

/// <summary>What the people list is being asked for.</summary>
public sealed record PeopleQuery(
    PageRequest Paging,
    string? Search,
    Guid? BranchId,
    Guid? DepartmentId,
    Guid? TeamId,
    bool ActiveOnly,
    bool UnlinkedOnly,
    string Language);

/// <summary>A person as the list shows them.</summary>
public sealed record PersonSummary(
    Guid Id,
    string DisplayName,
    string Email,
    PersonStatus Status,
    bool IsActive,
    bool HasSignedIn,
    PlacementView Placement);

/// <summary>A person as the detail screen shows them.</summary>
public sealed record PersonDetail(
    PersonSummary Summary,
    IReadOnlyList<RoleView> Roles,
    IReadOnlySet<string> EffectivePermissions,
    DateTimeOffset? LastSignedInAt);

/// <summary>
/// Derived from whether an identity is bound and whether the person is enabled, never stored -
/// two columns already carry the truth, and a third could disagree with them.
/// </summary>
public enum PersonStatus
{
    Invited = 0,
    Active = 1,
    Inactive = 2,
}

/// <summary>
/// Placement with the unit names in both languages, so a list can show them in the reader's
/// language without a second call - the shape feature 003 published for exactly this.
/// </summary>
public sealed record PlacementView(
    Guid? BranchId,
    string? BranchNameAr,
    string? BranchNameEn,
    Guid? DepartmentId,
    string? DepartmentNameAr,
    string? DepartmentNameEn,
    Guid? TeamId,
    string? TeamNameAr,
    string? TeamNameEn)
{
    public static PlacementView None => new(null, null, null, null, null, null, null, null, null);
}

public sealed record RoleView(Guid Id, string Name);

/// <summary>Creating somebody who has not arrived yet.</summary>
public sealed record PreProvisionCommand(
    Guid ActorId,
    string Email,
    string DisplayName,
    IReadOnlyList<Guid> RoleIds,
    PlacementCommand? Placement);

/// <summary>All three may be null, which clears the placement.</summary>
public sealed record PlacementCommand(Guid? BranchId, Guid? DepartmentId, Guid? TeamId);

/// <summary>The outcome of a write: the person as it now stands, or why it was refused.</summary>
public sealed record PersonWriteResult(PersonDetail? Person, PersonRefusal Refusal)
{
    public static PersonWriteResult Success(PersonDetail person) => new(person, PersonRefusal.None);

    public static PersonWriteResult Refused(PersonRefusal refusal) => new(null, refusal);

    public bool IsSuccess => Refusal == PersonRefusal.None;
}

/// <summary>A deletion, and the roles the person held immediately before it.</summary>
public sealed record PersonDeletionResult(
    bool Deleted,
    PersonRefusal Refusal,
    IReadOnlyList<RoleView> RolesHeldBeforeDeletion);

/// <summary>Why a write was refused. Mapped to an error code at the edge.</summary>
public enum PersonRefusal
{
    None = 0,
    NotFound = 1,

    /// <summary><c>identity_email_in_use</c></summary>
    EmailInUse = 2,

    /// <summary><c>identity_last_administrator</c></summary>
    LastAdministrator = 3,

    /// <summary><c>identity_self_demotion</c></summary>
    SelfDemotion = 4,

    /// <summary><c>identity_placement_mismatch</c></summary>
    PlacementMismatch = 5,

    /// <summary><c>organization_department_inactive</c> - a unit that is not active was chosen.</summary>
    UnitInactive = 6,
}
