using Crm.Application.Abstractions;
using Crm.Application.Common;

namespace Crm.Application.Identity.People;

/// <summary>
/// Administering people: who exists, what they may do, and where they sit.
/// </summary>
/// <remarks>
/// Every mutation is audited (AR-005 to AR-007). Auditing lives here rather than in the controller
/// because Constitution I keeps rules out of HTTP concerns, and "this change is worth recording" is
/// a rule - the same reasoning feature 003 applied to its organization service.
///
/// The refusals themselves come from the store, because two of them can only be decided inside the
/// transaction that makes them true.
/// </remarks>
public sealed class PeopleService(
    IPeopleStore store,
    IAuditRecorder audit,
    ICurrentUser currentUser,
    ICorrelationAccessor correlation,
    TimeProvider clock)
{
    public Task<PagedResult<PersonSummary>> ListAsync(
        PeopleQuery query,
        CancellationToken cancellationToken = default) =>
        store.ListAsync(query, cancellationToken);

    public Task<PersonDetail?> FindAsync(Guid personId, CancellationToken cancellationToken = default) =>
        store.FindAsync(personId, cancellationToken);

    public async Task<PersonWriteResult> PreProvisionAsync(
        string email,
        string displayName,
        IReadOnlyList<Guid> roleIds,
        PlacementCommand? placement,
        CancellationToken cancellationToken = default)
    {
        var actorId = ActorId();

        var result = await store.PreProvisionAsync(
            new PreProvisionCommand(actorId, email, displayName, roleIds, placement),
            cancellationToken);

        if (result.IsSuccess)
        {
            // The address is recorded because it is what identifies a person who has no identifier
            // of their own yet - there is no subject to name them by until they arrive.
            await RecordAsync(
                "identity.person.prepared",
                result.Person!.Summary.Id,
                new Dictionary<string, string> { ["email"] = result.Person.Summary.Email },
                cancellationToken);
        }

        return result;
    }

    public async Task<PersonWriteResult> GrantRoleAsync(
        Guid personId,
        Guid roleId,
        CancellationToken cancellationToken = default)
    {
        var result = await store.GrantRoleAsync(ActorId(), personId, roleId, cancellationToken);

        if (result.IsSuccess)
        {
            await RecordAsync(
                "identity.role.granted",
                personId,
                new Dictionary<string, string> { ["roleId"] = roleId.ToString() },
                cancellationToken);
        }

        return result;
    }

    public async Task<PersonWriteResult> RevokeRoleAsync(
        Guid personId,
        Guid roleId,
        CancellationToken cancellationToken = default)
    {
        var result = await store.RevokeRoleAsync(ActorId(), personId, roleId, cancellationToken);

        if (result.IsSuccess)
        {
            await RecordAsync(
                "identity.role.revoked",
                personId,
                new Dictionary<string, string> { ["roleId"] = roleId.ToString() },
                cancellationToken);
        }

        return result;
    }

    public async Task<PersonWriteResult> SetPlacementAsync(
        Guid personId,
        PlacementCommand placement,
        CancellationToken cancellationToken = default)
    {
        // Read the placement before changing it: AR-006 wants both sides, and afterwards the old
        // one exists nowhere.
        var before = await store.FindAsync(personId, cancellationToken);

        var result = await store.SetPlacementAsync(ActorId(), personId, placement, cancellationToken);

        if (result.IsSuccess)
        {
            await RecordAsync(
                "identity.placement.changed",
                personId,
                new Dictionary<string, string>
                {
                    ["from"] = Describe(before?.Summary.Placement),
                    ["to"] = Describe(result.Person!.Summary.Placement),
                },
                cancellationToken);
        }

        return result;
    }

    public async Task<PersonWriteResult> SetActivationAsync(
        Guid personId,
        bool isActive,
        CancellationToken cancellationToken = default)
    {
        var result = await store.SetActivationAsync(ActorId(), personId, isActive, cancellationToken);

        if (result.IsSuccess)
        {
            await RecordAsync(
                isActive ? "identity.person.activated" : "identity.person.deactivated",
                personId,
                metadata: null,
                cancellationToken);
        }

        return result;
    }

    public async Task<PersonDeletionResult> DeleteAsync(
        Guid personId,
        CancellationToken cancellationToken = default)
    {
        var result = await store.DeleteAsync(ActorId(), personId, cancellationToken);

        if (result.Deleted)
        {
            // The roles are recorded because revoking them destroyed the only other trace.
            // RoleAssignment has no revocation history, so without this line FR-025's history does
            // not exist anywhere.
            await RecordAsync(
                "identity.person.deleted",
                personId,
                new Dictionary<string, string>
                {
                    ["rolesHeld"] = result.RolesHeldBeforeDeletion.Count == 0
                        ? "none"
                        : string.Join(", ", result.RolesHeldBeforeDeletion.Select(role => role.Name)),
                },
                cancellationToken);
        }

        return result;
    }

    /// <summary>
    /// A compact, readable description of a placement for the audit trail.
    /// </summary>
    /// <remarks>
    /// Identifiers rather than names, deliberately. A unit can be renamed, and a trail that recorded
    /// the old name would quietly disagree with the current one - the same reasoning feature 003
    /// used when it recorded unit identifiers rather than codes.
    /// </remarks>
    private static string Describe(PlacementView? placement) =>
        placement is null
            ? "none"
            : $"branch={placement.BranchId?.ToString() ?? "none"}; " +
              $"department={placement.DepartmentId?.ToString() ?? "none"}; " +
              $"team={placement.TeamId?.ToString() ?? "none"}";

    private Guid ActorId() => currentUser.UserId ?? Guid.Empty;

    private Task RecordAsync(
        string action,
        Guid personId,
        IReadOnlyDictionary<string, string>? metadata,
        CancellationToken cancellationToken) =>
        audit.RecordAsync(
            new AuditEntry(
                action,
                currentUser.UserId,
                "User",
                personId.ToString(),
                clock.GetUtcNow(),
                correlation.CorrelationId,
                metadata),
            cancellationToken);
}
