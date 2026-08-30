using System.Data;
using Crm.Application.Abstractions;
using Crm.Application.Authorization;
using Crm.Application.Common;
using Crm.Application.Identity.People;
using Crm.Domain.Identity;
using Crm.Infrastructure.Persistence;
using Crm.Infrastructure.Persistence.Configurations;
using Microsoft.EntityFrameworkCore;

namespace Crm.Infrastructure.Identity;

/// <summary>
/// Reads and writes people for the administration surface (feature 004).
///
/// Every query relies on the global soft-delete filter from feature 001, so no call site writes
/// <c>WHERE IsDeleted = 0</c> by hand and none can forget to.
/// </summary>
/// <remarks>
/// Every write that could reduce administrator access runs inside a serializable transaction. The
/// isolation is not decoration: the guard is a read-then-write over a count, and two administrators
/// acting at the same instant each read a safe number and together produce an unsafe result. No
/// index can express "at least one row must remain", so isolation is where the guarantee comes
/// from (research decision 5).
/// </remarks>
public sealed class PeopleStore(CrmDbContext context, TimeProvider clock) : IPeopleStore
{
    public async Task<PagedResult<PersonSummary>> ListAsync(
        PeopleQuery query,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(query);

        var filtered = Filter(context.Users.AsNoTracking(), query);
        var total = await filtered.LongCountAsync(cancellationToken);

        // Ordered by the one name a person has. Unlike an organization unit they do not carry one
        // per language, so the order is the same for every reader (spec LR-002).
        var rows = await filtered
            .OrderBy(user => user.DisplayName)
            .ThenBy(user => user.Id)
            .Skip(query.Paging.Skip)
            .Take(query.Paging.PageSize)
            .Select(user => new PersonRow(
                user.Id,
                user.DisplayName,
                user.Email,
                user.IsActive,
                user.ProviderSubject != null,
                user.BranchId,
                user.DepartmentId,
                user.TeamId))
            .ToListAsync(cancellationToken);

        var placements = await ResolvePlacementsAsync(rows, cancellationToken);

        return new PagedResult<PersonSummary>(
            [.. rows.Select(row => ToSummary(row, placements))],
            query.Paging.Page,
            query.Paging.PageSize,
            total);
    }

    public async Task<PersonDetail?> FindAsync(Guid personId, CancellationToken cancellationToken = default)
    {
        var row = await context.Users
            .AsNoTracking()
            .Where(user => user.Id == personId)
            .Select(user => new PersonRow(
                user.Id,
                user.DisplayName,
                user.Email,
                user.IsActive,
                user.ProviderSubject != null,
                user.BranchId,
                user.DepartmentId,
                user.TeamId))
            .FirstOrDefaultAsync(cancellationToken);

        if (row is null)
        {
            return null;
        }

        var lastSignedInAt = await context.Users
            .AsNoTracking()
            .Where(user => user.Id == personId)
            .Select(user => user.LastSignedInAt)
            .FirstOrDefaultAsync(cancellationToken);

        return await ToDetailAsync(row, lastSignedInAt, cancellationToken);
    }

    public async Task<PersonWriteResult> PreProvisionAsync(
        PreProvisionCommand command,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(command);

        var normalized = User.NormalizeEmail(command.Email);

        // The filtered index refuses this too, but a read first turns a database exception into a
        // refusal the client can translate. Both matter: the index is what holds under a race.
        if (await context.Users.AnyAsync(user => user.Email == normalized, cancellationToken))
        {
            return PersonWriteResult.Refused(PersonRefusal.EmailInUse);
        }

        var person = User.PreProvision(normalized, command.DisplayName, (int)CallerPopulation.Staff);

        if (command.Placement is { } placement)
        {
            var applied = await ApplyPlacementAsync(person, placement, cancellationToken);

            if (applied != PersonRefusal.None)
            {
                return PersonWriteResult.Refused(applied);
            }
        }

        context.Users.Add(person);

        foreach (var roleId in command.RoleIds.Distinct())
        {
            context.RoleAssignments.Add(new RoleAssignment
            {
                UserId = person.Id,
                RoleId = roleId,
                GrantedAt = clock.GetUtcNow(),
                GrantedBy = command.ActorId,
            });
        }

        await context.SaveChangesAsync(cancellationToken);

        return await ReadBackAsync(person.Id, cancellationToken);
    }

    public async Task<PersonWriteResult> GrantRoleAsync(
        Guid actorId,
        Guid personId,
        Guid roleId,
        CancellationToken cancellationToken = default)
    {
        var person = await context.Users.FirstOrDefaultAsync(user => user.Id == personId, cancellationToken);

        if (person is null)
        {
            return PersonWriteResult.Refused(PersonRefusal.NotFound);
        }

        // Idempotent by the schema as well as by this check: the composite key refuses a duplicate
        // regardless of what a caller believed (spec FR-008).
        var alreadyHeld = await context.RoleAssignments
            .AnyAsync(a => a.UserId == personId && a.RoleId == roleId, cancellationToken);

        if (!alreadyHeld)
        {
            context.RoleAssignments.Add(new RoleAssignment
            {
                UserId = personId,
                RoleId = roleId,
                GrantedAt = clock.GetUtcNow(),
                GrantedBy = actorId,
            });

            await context.SaveChangesAsync(cancellationToken);
        }

        return await ReadBackAsync(personId, cancellationToken);
    }

    public async Task<PersonWriteResult> RevokeRoleAsync(
        Guid actorId,
        Guid personId,
        Guid roleId,
        CancellationToken cancellationToken = default)
    {
        await using var transaction = await context.Database.BeginTransactionAsync(
            IsolationLevel.Serializable,
            cancellationToken);

        var person = await context.Users.FirstOrDefaultAsync(user => user.Id == personId, cancellationToken);

        if (person is null)
        {
            return PersonWriteResult.Refused(PersonRefusal.NotFound);
        }

        var refusal = await GuardAsync(actorId, personId, cancellationToken);

        if (refusal != PersonRefusal.None)
        {
            return PersonWriteResult.Refused(refusal);
        }

        var assignment = await context.RoleAssignments
            .FirstOrDefaultAsync(a => a.UserId == personId && a.RoleId == roleId, cancellationToken);

        if (assignment is not null)
        {
            context.RoleAssignments.Remove(assignment);
            await context.SaveChangesAsync(cancellationToken);
        }

        await transaction.CommitAsync(cancellationToken);

        return await ReadBackAsync(personId, cancellationToken);
    }

    public async Task<PersonWriteResult> SetPlacementAsync(
        Guid actorId,
        Guid personId,
        PlacementCommand placement,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(placement);

        var person = await context.Users.FirstOrDefaultAsync(user => user.Id == personId, cancellationToken);

        if (person is null)
        {
            return PersonWriteResult.Refused(PersonRefusal.NotFound);
        }

        var refusal = await ApplyPlacementAsync(person, placement, cancellationToken);

        if (refusal != PersonRefusal.None)
        {
            return PersonWriteResult.Refused(refusal);
        }

        await context.SaveChangesAsync(cancellationToken);

        return await ReadBackAsync(personId, cancellationToken);
    }

    public async Task<PersonWriteResult> SetActivationAsync(
        Guid actorId,
        Guid personId,
        bool isActive,
        CancellationToken cancellationToken = default)
    {
        await using var transaction = await context.Database.BeginTransactionAsync(
            IsolationLevel.Serializable,
            cancellationToken);

        var person = await context.Users.FirstOrDefaultAsync(user => user.Id == personId, cancellationToken);

        if (person is null)
        {
            return PersonWriteResult.Refused(PersonRefusal.NotFound);
        }

        if (!isActive)
        {
            var refusal = await GuardAsync(actorId, personId, cancellationToken);

            if (refusal != PersonRefusal.None)
            {
                return PersonWriteResult.Refused(refusal);
            }

            person.Deactivate();

            // Access that ends at the next renewal has not ended. Token validation resolves the
            // session on every request, so revoking lands on the next one (spec FR-023).
            await RevokeSessionsAsync(personId, cancellationToken);
        }
        else
        {
            person.Reactivate();
        }

        await context.SaveChangesAsync(cancellationToken);
        await transaction.CommitAsync(cancellationToken);

        return await ReadBackAsync(personId, cancellationToken);
    }

    public async Task<PersonDeletionResult> DeleteAsync(
        Guid actorId,
        Guid personId,
        CancellationToken cancellationToken = default)
    {
        await using var transaction = await context.Database.BeginTransactionAsync(
            IsolationLevel.Serializable,
            cancellationToken);

        var person = await context.Users.FirstOrDefaultAsync(user => user.Id == personId, cancellationToken);

        if (person is null)
        {
            return new PersonDeletionResult(false, PersonRefusal.NotFound, []);
        }

        var refusal = await GuardAsync(actorId, personId, cancellationToken);

        if (refusal != PersonRefusal.None)
        {
            return new PersonDeletionResult(false, refusal, []);
        }

        // Read the roles before revoking them. RoleAssignment has no revocation history - revoking
        // deletes the only row that ever recorded the grant - so if these are not carried into the
        // audit entry, that history exists nowhere (spec FR-025).
        var held = await ReadRolesAsync(personId, cancellationToken);

        var assignments = await context.RoleAssignments
            .Where(assignment => assignment.UserId == personId)
            .ToListAsync(cancellationToken);

        // Tracked entities rather than ExecuteUpdateAsync throughout: a set-based write bypasses
        // AuditingSaveChangesInterceptor, so the operation that most needs a trail would be the one
        // that stopped writing one (research decision 4, and feature 003's identical finding).
        context.RoleAssignments.RemoveRange(assignments);

        await RevokeSessionsAsync(personId, cancellationToken);

        person.IsDeleted = true;
        person.DeletedAt = clock.GetUtcNow();
        person.DeletedBy = actorId;

        await context.SaveChangesAsync(cancellationToken);
        await transaction.CommitAsync(cancellationToken);

        return new PersonDeletionResult(true, PersonRefusal.None, held);
    }

    /// <summary>
    /// Gathers the facts the guard needs and asks it. Called only from inside a serializable
    /// transaction, which is what keeps the count true between reading it and acting on it.
    /// </summary>
    private async Task<PersonRefusal> GuardAsync(
        Guid actorId,
        Guid personId,
        CancellationToken cancellationToken)
    {
        var administrators = await context.RoleAssignments
            .Where(assignment => assignment.RoleId == IdentitySeed.AdministratorRoleId)
            .Join(
                context.Users.Where(user => user.IsActive),
                assignment => assignment.UserId,
                user => user.Id,
                (assignment, user) => user.Id)
            .ToListAsync(cancellationToken);

        var change = new AdministratorChange(
            actorId,
            personId,
            administrators.Contains(personId),
            administrators.Count(id => id != personId));

        return AdministratorGuard.Check(change) switch
        {
            AdministratorGuardResult.LastAdministrator => PersonRefusal.LastAdministrator,
            AdministratorGuardResult.SelfDemotion => PersonRefusal.SelfDemotion,
            _ => PersonRefusal.None,
        };
    }

    private async Task RevokeSessionsAsync(Guid personId, CancellationToken cancellationToken)
    {
        var sessions = await context.Sessions
            .Where(session => session.UserId == personId && session.RevokedAt == null)
            .ToListAsync(cancellationToken);

        var now = clock.GetUtcNow();

        foreach (var session in sessions)
        {
            session.Revoke(now, SessionRevocationReason.UserDeactivated);
        }
    }

    /// <summary>
    /// Applies a placement to a tracked person, deriving the department from the team when one is
    /// given and refusing anything that is not active (spec FR-010, FR-011, FR-012).
    /// </summary>
    private async Task<PersonRefusal> ApplyPlacementAsync(
        User person,
        PlacementCommand placement,
        CancellationToken cancellationToken)
    {
        if (placement.BranchId is { } branchId
            && !await context.Branches.AnyAsync(b => b.Id == branchId && b.IsActive, cancellationToken))
        {
            return PersonRefusal.UnitInactive;
        }

        if (placement.TeamId is { } teamId)
        {
            var team = await context.Teams
                .Where(t => t.Id == teamId && t.IsActive)
                .Select(t => new { t.Id, t.DepartmentId })
                .FirstOrDefaultAsync(cancellationToken);

            if (team is null)
            {
                return PersonRefusal.UnitInactive;
            }

            // Refused rather than silently corrected: a caller sending a stale department has a bug,
            // and storing something it did not ask for hides it.
            if (placement.DepartmentId is { } named && named != team.DepartmentId)
            {
                return PersonRefusal.PlacementMismatch;
            }

            person.Place(placement.BranchId, null, new TeamPlacement(team.Id, team.DepartmentId));

            return PersonRefusal.None;
        }

        if (placement.DepartmentId is { } departmentId
            && !await context.Departments.AnyAsync(d => d.Id == departmentId && d.IsActive, cancellationToken))
        {
            return PersonRefusal.UnitInactive;
        }

        person.Place(placement.BranchId, placement.DepartmentId, null);

        return PersonRefusal.None;
    }

    private async Task<PersonWriteResult> ReadBackAsync(Guid personId, CancellationToken cancellationToken)
    {
        var detail = await FindAsync(personId, cancellationToken);

        return detail is null
            ? PersonWriteResult.Refused(PersonRefusal.NotFound)
            : PersonWriteResult.Success(detail);
    }

    private async Task<PersonDetail> ToDetailAsync(
        PersonRow row,
        DateTimeOffset? lastSignedInAt,
        CancellationToken cancellationToken)
    {
        var placements = await ResolvePlacementsAsync([row], cancellationToken);
        var roles = await ReadRolesAsync(row.Id, cancellationToken);

        var granted = await context.RoleAssignments
            .Where(assignment => assignment.UserId == row.Id)
            .Join(
                context.RolePermissions,
                assignment => assignment.RoleId,
                grant => grant.RoleId,
                (_, grant) => grant.Permission)
            .Distinct()
            .ToListAsync(cancellationToken);

        // The union of what the roles grant, with anything no longer in the catalog dropped: a
        // permission the product no longer has is not a permission this person effectively holds.
        var effective = granted
            .Where(Permissions.Exists)
            .ToHashSet(StringComparer.Ordinal);

        return new PersonDetail(ToSummary(row, placements), roles, effective, lastSignedInAt);
    }

    private async Task<IReadOnlyList<RoleView>> ReadRolesAsync(Guid personId, CancellationToken cancellationToken) =>
        await context.RoleAssignments
            .AsNoTracking()
            .Where(assignment => assignment.UserId == personId)
            .Join(
                context.Roles,
                assignment => assignment.RoleId,
                role => role.Id,
                (_, role) => new RoleView(role.Id, role.Name))
            .OrderBy(role => role.Name)
            .ToListAsync(cancellationToken);

    /// <summary>
    /// Looks up the unit names for a page of people in three queries rather than three per row.
    /// </summary>
    private async Task<PlacementNames> ResolvePlacementsAsync(
        IReadOnlyList<PersonRow> rows,
        CancellationToken cancellationToken)
    {
        var branchIds = rows.Where(r => r.BranchId is not null).Select(r => r.BranchId!.Value).Distinct().ToList();
        var departmentIds = rows.Where(r => r.DepartmentId is not null).Select(r => r.DepartmentId!.Value).Distinct().ToList();
        var teamIds = rows.Where(r => r.TeamId is not null).Select(r => r.TeamId!.Value).Distinct().ToList();

        var branches = await context.Branches
            .AsNoTracking()
            .Where(b => branchIds.Contains(b.Id))
            .ToDictionaryAsync(b => b.Id, b => (b.NameAr, b.NameEn), cancellationToken);

        var departments = await context.Departments
            .AsNoTracking()
            .Where(d => departmentIds.Contains(d.Id))
            .ToDictionaryAsync(d => d.Id, d => (d.NameAr, d.NameEn), cancellationToken);

        var teams = await context.Teams
            .AsNoTracking()
            .Where(t => teamIds.Contains(t.Id))
            .ToDictionaryAsync(t => t.Id, t => (t.NameAr, t.NameEn), cancellationToken);

        return new PlacementNames(branches, departments, teams);
    }

    private static PersonSummary ToSummary(PersonRow row, PlacementNames names) =>
        new(
            row.Id,
            row.DisplayName,
            row.Email,
            Status(row),
            row.IsActive,
            row.HasSignedIn,
            names.For(row));

    /// <summary>Derived from two columns that cannot disagree with themselves. Never stored.</summary>
    private static PersonStatus Status(PersonRow row) =>
        !row.IsActive ? PersonStatus.Inactive
        : row.HasSignedIn ? PersonStatus.Active
        : PersonStatus.Invited;

    private static IQueryable<User> Filter(IQueryable<User> source, PeopleQuery query)
    {
        if (!string.IsNullOrWhiteSpace(query.Search))
        {
            var search = query.Search.Trim();

            source = source.Where(user =>
                user.DisplayName.Contains(search) || user.Email.Contains(search));
        }

        if (query.BranchId is { } branchId)
        {
            source = source.Where(user => user.BranchId == branchId);
        }

        if (query.DepartmentId is { } departmentId)
        {
            source = source.Where(user => user.DepartmentId == departmentId);
        }

        if (query.TeamId is { } teamId)
        {
            source = source.Where(user => user.TeamId == teamId);
        }

        if (query.ActiveOnly)
        {
            source = source.Where(user => user.IsActive);
        }

        // "Who has been prepared and not yet arrived" - the question pre-provisioning creates.
        if (query.UnlinkedOnly)
        {
            source = source.Where(user => user.ProviderSubject == null);
        }

        return source;
    }

    private sealed record PersonRow(
        Guid Id,
        string DisplayName,
        string Email,
        bool IsActive,
        bool HasSignedIn,
        Guid? BranchId,
        Guid? DepartmentId,
        Guid? TeamId);

    private sealed record PlacementNames(
        Dictionary<Guid, (string NameAr, string NameEn)> Branches,
        Dictionary<Guid, (string NameAr, string NameEn)> Departments,
        Dictionary<Guid, (string NameAr, string NameEn)> Teams)
    {
        public PlacementView For(PersonRow row)
        {
            var branch = Look(Branches, row.BranchId);
            var department = Look(Departments, row.DepartmentId);
            var team = Look(Teams, row.TeamId);

            return new PlacementView(
                row.BranchId, branch.NameAr, branch.NameEn,
                row.DepartmentId, department.NameAr, department.NameEn,
                row.TeamId, team.NameAr, team.NameEn);
        }

        private static (string? NameAr, string? NameEn) Look(
            Dictionary<Guid, (string NameAr, string NameEn)> source,
            Guid? id) =>
            id is { } key && source.TryGetValue(key, out var found) ? found : (null, null);
    }
}
