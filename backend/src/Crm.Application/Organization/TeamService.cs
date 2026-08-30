using Crm.Application.Abstractions;
using Crm.Application.Common;
using Crm.Domain.Organization;

namespace Crm.Application.Organization;

/// <summary>
/// Maintaining teams. Separate from <see cref="OrganizationUnitService"/> because a team is the one
/// unit with a containment rule, and that rule shows up in every operation: it is created inside a
/// department, its name is unique only within that department, and it can be moved to another one.
/// </summary>
public sealed class TeamService(
    IOrganizationStore store,
    IAuditRecorder audit,
    ICurrentUser currentUser,
    ICorrelationAccessor correlation,
    TimeProvider clock)
{
    public Task<PagedResult<TeamRecord>> ListAsync(
        Guid departmentId,
        UnitListQuery query,
        CancellationToken cancellationToken = default) =>
        store.ListTeamsAsync(departmentId, query, cancellationToken);

    public Task<TeamRecord?> FindAsync(Guid id, CancellationToken cancellationToken = default) =>
        store.FindTeamAsync(id, cancellationToken);

    public async Task<OrganizationOutcome<TeamRecord>> CreateAsync(
        Guid departmentId,
        string nameAr,
        string nameEn,
        string code,
        CancellationToken cancellationToken = default)
    {
        if (await store.FindAsync<Department>(departmentId, cancellationToken) is null)
        {
            return OrganizationOutcome.Refused<TeamRecord>(OrganizationRefusal.NotFound);
        }

        // Team codes are unique across all teams; team names only within their department. The
        // asymmetry is the clarified decision, not an oversight.
        if (await store.CodeExistsAsync<Team>(code, cancellationToken))
        {
            return OrganizationOutcome.Refused<TeamRecord>(
                OrganizationRefusal.CodeConflict,
                OrganizationUnit.Normalize(code));
        }

        if (await store.TeamNameExistsInDepartmentAsync(
            departmentId, nameAr, nameEn, null, cancellationToken))
        {
            return OrganizationOutcome.Refused<TeamRecord>(OrganizationRefusal.NameConflict);
        }

        var team = await store.CreateTeamAsync(departmentId, nameAr, nameEn, code, cancellationToken);

        if (team is null)
        {
            return OrganizationOutcome.Refused<TeamRecord>(OrganizationRefusal.NotFound);
        }

        await RecordAsync("created", team.Id, cancellationToken);

        return OrganizationOutcome.Success(team);
    }

    public async Task<OrganizationOutcome<TeamRecord>> RenameAsync(
        Guid id,
        string nameAr,
        string nameEn,
        CancellationToken cancellationToken = default)
    {
        var existing = await store.FindTeamAsync(id, cancellationToken);

        if (existing is null)
        {
            return OrganizationOutcome.Refused<TeamRecord>(OrganizationRefusal.NotFound);
        }

        if (await store.TeamNameExistsInDepartmentAsync(
            existing.DepartmentId, nameAr, nameEn, id, cancellationToken))
        {
            return OrganizationOutcome.Refused<TeamRecord>(OrganizationRefusal.NameConflict);
        }

        await store.RenameAsync<Team>(id, nameAr, nameEn, cancellationToken);
        await RecordAsync("renamed", id, cancellationToken);

        return OrganizationOutcome.Success((await store.FindTeamAsync(id, cancellationToken))!);
    }

    public async Task<OrganizationOutcome<TeamRecord>> SetActivationAsync(
        Guid id,
        bool isActive,
        CancellationToken cancellationToken = default)
    {
        if (await store.SetActivationAsync<Team>(id, isActive, cancellationToken) is null)
        {
            return OrganizationOutcome.Refused<TeamRecord>(OrganizationRefusal.NotFound);
        }

        await RecordAsync(isActive ? "activated" : "deactivated", id, cancellationToken);

        return OrganizationOutcome.Success((await store.FindTeamAsync(id, cancellationToken))!);
    }

    public async Task<OrganizationOutcome<bool>> DeleteAsync(
        Guid id,
        CancellationToken cancellationToken = default)
    {
        if (await store.FindTeamAsync(id, cancellationToken) is null)
        {
            return OrganizationOutcome.Refused<bool>(OrganizationRefusal.NotFound);
        }

        var dependents = await store.CountDependentsAsync<Team>(id, cancellationToken);

        if (dependents.Any)
        {
            return OrganizationOutcome.Refused<bool>(
                OrganizationRefusal.HasDependents,
                OrganizationUnitService.Describe(dependents));
        }

        await store.DeleteAsync<Team>(id, cancellationToken);
        await RecordAsync("deleted", id, cancellationToken);

        return OrganizationOutcome.Success(true);
    }

    /// <summary>
    /// Moves a team to another department, carrying its members with it (spec FR-015).
    /// </summary>
    /// <remarks>
    /// Three refusals come before the move, and their order matters. A move to the department the
    /// team is already in is accepted first and changes nothing, so re-submitting is never an error
    /// - not even when that department has since been deactivated.
    /// </remarks>
    public async Task<OrganizationOutcome<TeamMoveResult>> MoveAsync(
        Guid id,
        Guid destinationDepartmentId,
        CancellationToken cancellationToken = default)
    {
        var team = await store.FindTeamAsync(id, cancellationToken);

        if (team is null)
        {
            return OrganizationOutcome.Refused<TeamMoveResult>(OrganizationRefusal.NotFound);
        }

        if (team.DepartmentId == destinationDepartmentId)
        {
            return OrganizationOutcome.Success(new TeamMoveResult(team, 0));
        }

        var destination = await store.FindAsync<Department>(destinationDepartmentId, cancellationToken);

        if (destination is null)
        {
            return OrganizationOutcome.Refused<TeamMoveResult>(OrganizationRefusal.NotFound);
        }

        if (!destination.IsActive)
        {
            return OrganizationOutcome.Refused<TeamMoveResult>(
                OrganizationRefusal.DepartmentInactive,
                destination.NameEn);
        }

        // A team may keep its name when it moves, unless the destination already has one of that
        // name - which would break the per-department uniqueness the move is crossing into.
        if (await store.TeamNameExistsInDepartmentAsync(
            destinationDepartmentId, team.NameAr, team.NameEn, id, cancellationToken))
        {
            return OrganizationOutcome.Refused<TeamMoveResult>(OrganizationRefusal.NameConflict);
        }

        var result = await store.MoveTeamAsync(id, destinationDepartmentId, cancellationToken);

        if (result is null)
        {
            return OrganizationOutcome.Refused<TeamMoveResult>(OrganizationRefusal.NotFound);
        }

        // The audit carries both departments and the affected count (AR-006), because the team row
        // alone no longer shows where it came from once the move has happened.
        await audit.RecordAsync(
            new AuditEntry(
                "organization.team.moved",
                currentUser.UserId,
                nameof(Team),
                id.ToString(),
                clock.GetUtcNow(),
                correlation.CorrelationId,
                new Dictionary<string, string>(StringComparer.Ordinal)
                {
                    ["fromDepartmentId"] = team.DepartmentId.ToString(),
                    ["toDepartmentId"] = destinationDepartmentId.ToString(),
                    ["membersReassigned"] = result.MembersReassigned.ToString(
                        System.Globalization.CultureInfo.InvariantCulture),
                }),
            cancellationToken);

        return OrganizationOutcome.Success(result);
    }

    private Task RecordAsync(string action, Guid id, CancellationToken cancellationToken) =>
        audit.RecordAsync(
            new AuditEntry(
                $"organization.team.{action}",
                currentUser.UserId,
                nameof(Team),
                id.ToString(),
                clock.GetUtcNow(),
                correlation.CorrelationId),
            cancellationToken);
}
