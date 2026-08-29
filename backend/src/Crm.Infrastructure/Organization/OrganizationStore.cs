using Crm.Application.Abstractions;
using Crm.Application.Common;
using Crm.Domain.Identity;
using Crm.Domain.Organization;
using Crm.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;

namespace Crm.Infrastructure.Organization;

/// <summary>
/// Reads and writes organizational structure.
///
/// Every query here relies on the global soft-delete filter from feature 001, so no call site
/// writes <c>WHERE IsDeleted = 0</c> by hand and none can forget to.
/// </summary>
public sealed class OrganizationStore(CrmDbContext context) : IOrganizationStore
{
    /// <summary>Fields a list endpoint may sort by, per the pagination contract.</summary>
    public static IReadOnlySet<string> SortableFields { get; } =
        new HashSet<string>(StringComparer.OrdinalIgnoreCase) { "nameAr", "nameEn", "code" };

    public async Task<PagedResult<OrganizationUnitRecord>> ListAsync<TUnit>(
        UnitListQuery query,
        CancellationToken cancellationToken = default)
        where TUnit : OrganizationUnit
    {
        ArgumentNullException.ThrowIfNull(query);

        var filtered = Filter(context.Set<TUnit>(), query);
        var total = await filtered.LongCountAsync(cancellationToken);

        var page = await Order(filtered, query)
            .Skip(query.Paging.Skip)
            .Take(query.Paging.PageSize)
            .Select(unit => new OrganizationUnitRecord(
                unit.Id,
                unit.NameAr,
                unit.NameEn,
                unit.Code,
                unit.IsActive))
            .ToListAsync(cancellationToken);

        return new PagedResult<OrganizationUnitRecord>(
            page,
            query.Paging.Page,
            query.Paging.PageSize,
            total);
    }

    public async Task<OrganizationUnitRecord?> FindAsync<TUnit>(
        Guid id,
        CancellationToken cancellationToken = default)
        where TUnit : OrganizationUnit =>
        await context.Set<TUnit>()
            .Where(unit => unit.Id == id)
            .Select(unit => new OrganizationUnitRecord(
                unit.Id,
                unit.NameAr,
                unit.NameEn,
                unit.Code,
                unit.IsActive))
            .FirstOrDefaultAsync(cancellationToken);

    public Task<bool> CodeExistsAsync<TUnit>(string code, CancellationToken cancellationToken = default)
        where TUnit : OrganizationUnit
    {
        var normalized = OrganizationUnit.Normalize(code);

        return context.Set<TUnit>().AnyAsync(unit => unit.Code == normalized, cancellationToken);
    }

    public Task<bool> NameExistsAsync<TUnit>(
        string nameAr,
        string nameEn,
        Guid? excluding = null,
        CancellationToken cancellationToken = default)
        where TUnit : OrganizationUnit
    {
        var ar = OrganizationUnit.Normalize(nameAr);
        var en = OrganizationUnit.Normalize(nameEn);

        return context.Set<TUnit>().AnyAsync(
            unit => (excluding == null || unit.Id != excluding)
                && (unit.NameAr == ar || unit.NameEn == en),
            cancellationToken);
    }

    public async Task<OrganizationUnitRecord> AddAsync<TUnit>(
        TUnit unit,
        CancellationToken cancellationToken = default)
        where TUnit : OrganizationUnit
    {
        ArgumentNullException.ThrowIfNull(unit);

        context.Set<TUnit>().Add(unit);
        await context.SaveChangesAsync(cancellationToken);

        return ToRecord(unit);
    }

    public async Task<OrganizationUnitRecord?> RenameAsync<TUnit>(
        Guid id,
        string nameAr,
        string nameEn,
        CancellationToken cancellationToken = default)
        where TUnit : OrganizationUnit
    {
        var unit = await context.Set<TUnit>()
            .FirstOrDefaultAsync(entry => entry.Id == id, cancellationToken);

        if (unit is null)
        {
            return null;
        }

        unit.Rename(nameAr, nameEn);
        await context.SaveChangesAsync(cancellationToken);

        return ToRecord(unit);
    }

    public async Task<OrganizationUnitRecord?> SetActivationAsync<TUnit>(
        Guid id,
        bool isActive,
        CancellationToken cancellationToken = default)
        where TUnit : OrganizationUnit
    {
        var unit = await context.Set<TUnit>()
            .FirstOrDefaultAsync(entry => entry.Id == id, cancellationToken);

        if (unit is null)
        {
            return null;
        }

        if (isActive)
        {
            unit.Activate();
        }
        else
        {
            unit.Deactivate();
        }

        await context.SaveChangesAsync(cancellationToken);

        return ToRecord(unit);
    }

    public async Task<bool> DeleteAsync<TUnit>(Guid id, CancellationToken cancellationToken = default)
        where TUnit : OrganizationUnit
    {
        var unit = await context.Set<TUnit>()
            .FirstOrDefaultAsync(entry => entry.Id == id, cancellationToken);

        if (unit is null)
        {
            return false;
        }

        // Remove() is turned into a soft delete by the interceptor from feature 001, so the row and
        // its audit history survive while the unit disappears from every list (spec FR-011).
        context.Set<TUnit>().Remove(unit);
        await context.SaveChangesAsync(cancellationToken);

        return true;
    }

    public async Task<DependentSummary> CountDependentsAsync<TUnit>(
        Guid id,
        CancellationToken cancellationToken = default)
        where TUnit : OrganizationUnit
    {
        var teams = typeof(TUnit) == typeof(Department)
            ? await context.Teams.CountAsync(team => team.DepartmentId == id, cancellationToken)
            : 0;

        var people = await CountPlacedPeopleAsync<TUnit>(id, cancellationToken);

        return new DependentSummary(teams, people);
    }

    public async Task<PagedResult<TeamRecord>> ListTeamsAsync(
        Guid departmentId,
        UnitListQuery query,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(query);

        var filtered = Filter(
            context.Teams.Where(team => team.DepartmentId == departmentId),
            query);

        var total = await filtered.LongCountAsync(cancellationToken);

        var page = await Order(filtered, query)
            .Skip(query.Paging.Skip)
            .Take(query.Paging.PageSize)
            .Join(
                context.Departments,
                team => team.DepartmentId,
                department => department.Id,
                (team, department) => new TeamRecord(
                    team.Id,
                    team.NameAr,
                    team.NameEn,
                    team.Code,
                    team.IsActive,
                    department.Id,
                    department.NameAr,
                    department.NameEn))
            .ToListAsync(cancellationToken);

        return new PagedResult<TeamRecord>(page, query.Paging.Page, query.Paging.PageSize, total);
    }

    public Task<TeamRecord?> FindTeamAsync(Guid id, CancellationToken cancellationToken = default) =>
        TeamRecords().FirstOrDefaultAsync(team => team.Id == id, cancellationToken);

    public Task<bool> TeamNameExistsInDepartmentAsync(
        Guid departmentId,
        string nameAr,
        string nameEn,
        Guid? excluding = null,
        CancellationToken cancellationToken = default)
    {
        var ar = OrganizationUnit.Normalize(nameAr);
        var en = OrganizationUnit.Normalize(nameEn);

        return context.Teams.AnyAsync(
            team => team.DepartmentId == departmentId
                && (excluding == null || team.Id != excluding)
                && (team.NameAr == ar || team.NameEn == en),
            cancellationToken);
    }

    public async Task<TeamRecord?> CreateTeamAsync(
        Guid departmentId,
        string nameAr,
        string nameEn,
        string code,
        CancellationToken cancellationToken = default)
    {
        var department = await context.Departments
            .FirstOrDefaultAsync(entry => entry.Id == departmentId, cancellationToken);

        if (department is null)
        {
            return null;
        }

        var team = Team.Create(department, nameAr, nameEn, code);

        context.Teams.Add(team);
        await context.SaveChangesAsync(cancellationToken);

        return new TeamRecord(
            team.Id,
            team.NameAr,
            team.NameEn,
            team.Code,
            team.IsActive,
            department.Id,
            department.NameAr,
            department.NameEn);
    }

    /// <summary>
    /// Moves a team and carries its members with it.
    ///
    /// The members are loaded and updated through the change tracker rather than by a set-based
    /// <c>ExecuteUpdateAsync</c>. That is deliberate and is the plan's Complexity Tracking entry:
    /// a set-based update bypasses <c>AuditingSaveChangesInterceptor</c>, so the operation that most
    /// needs a trail would be the only one that stopped writing <c>UpdatedAt</c> and
    /// <c>UpdatedBy</c>. A team's membership is tens of rows, so the efficient path buys nothing.
    ///
    /// Everything happens in one transaction: a partially applied move would leave members in a
    /// department their team has left, which is exactly the inconsistency INV-2 forbids.
    /// </summary>
    public async Task<TeamMoveResult?> MoveTeamAsync(
        Guid teamId,
        Guid destinationDepartmentId,
        CancellationToken cancellationToken = default)
    {
        var team = await context.Teams.FirstOrDefaultAsync(
            entry => entry.Id == teamId,
            cancellationToken);

        var destination = await context.Departments.FirstOrDefaultAsync(
            entry => entry.Id == destinationDepartmentId,
            cancellationToken);

        if (team is null || destination is null)
        {
            return null;
        }

        await using var transaction = await context.Database.BeginTransactionAsync(cancellationToken);

        // Throws when the destination is inactive (spec FR-016). The transaction is disposed
        // without committing, so nothing is half-applied.
        team.MoveTo(destination);

        var members = await context.Users
            .Where(user => user.TeamId == teamId)
            .ToListAsync(cancellationToken);

        foreach (var member in members)
        {
            member.PlaceInDepartment(destination.Id);
        }

        await context.SaveChangesAsync(cancellationToken);
        await transaction.CommitAsync(cancellationToken);

        var record = new TeamRecord(
            team.Id,
            team.NameAr,
            team.NameEn,
            team.Code,
            team.IsActive,
            destination.Id,
            destination.NameAr,
            destination.NameEn);

        return new TeamMoveResult(record, members.Count);
    }

    private static OrganizationUnitRecord ToRecord(OrganizationUnit unit) =>
        new(unit.Id, unit.NameAr, unit.NameEn, unit.Code, unit.IsActive);

    private static IQueryable<TUnit> Filter<TUnit>(IQueryable<TUnit> source, UnitListQuery query)
        where TUnit : OrganizationUnit
    {
        if (query.ActiveOnly)
        {
            source = source.Where(unit => unit.IsActive);
        }

        if (!string.IsNullOrWhiteSpace(query.Search))
        {
            var search = query.Search.Trim();

            source = source.Where(unit =>
                unit.NameAr.Contains(search)
                || unit.NameEn.Contains(search)
                || unit.Code.Contains(search));
        }

        return source;
    }

    /// <summary>
    /// Orders by the requested field, defaulting to the name in the reader's language rather than
    /// always to English (spec LR-002). The identifier tiebreaker keeps paging stable: without it,
    /// equal names can shuffle between requests and a row is skipped or repeated.
    /// </summary>
    private static IQueryable<TUnit> Order<TUnit>(IQueryable<TUnit> source, UnitListQuery query)
        where TUnit : OrganizationUnit
    {
        var sort = query.Paging.ParseSort();
        var arabic = query.Language.StartsWith("ar", StringComparison.OrdinalIgnoreCase);

        var ordered = sort switch
        {
            { Field: var field, Descending: var descending }
                when field.Equals("nameAr", StringComparison.OrdinalIgnoreCase) =>
                descending
                    ? source.OrderByDescending(unit => unit.NameAr)
                    : source.OrderBy(unit => unit.NameAr),

            { Field: var field, Descending: var descending }
                when field.Equals("nameEn", StringComparison.OrdinalIgnoreCase) =>
                descending
                    ? source.OrderByDescending(unit => unit.NameEn)
                    : source.OrderBy(unit => unit.NameEn),

            { Field: var field, Descending: var descending }
                when field.Equals("code", StringComparison.OrdinalIgnoreCase) =>
                descending
                    ? source.OrderByDescending(unit => unit.Code)
                    : source.OrderBy(unit => unit.Code),

            _ => arabic ? source.OrderBy(unit => unit.NameAr) : source.OrderBy(unit => unit.NameEn),
        };

        return ordered.ThenBy(unit => unit.Id);
    }

    private IQueryable<TeamRecord> TeamRecords() =>
        context.Teams.Join(
            context.Departments,
            team => team.DepartmentId,
            department => department.Id,
            (team, department) => new TeamRecord(
                team.Id,
                team.NameAr,
                team.NameEn,
                team.Code,
                team.IsActive,
                department.Id,
                department.NameAr,
                department.NameEn));

    private Task<int> CountPlacedPeopleAsync<TUnit>(Guid id, CancellationToken cancellationToken)
        where TUnit : OrganizationUnit
    {
        if (typeof(TUnit) == typeof(Department))
        {
            return context.Users.CountAsync(user => user.DepartmentId == id, cancellationToken);
        }

        if (typeof(TUnit) == typeof(Branch))
        {
            return context.Users.CountAsync(user => user.BranchId == id, cancellationToken);
        }

        return context.Users.CountAsync(user => user.TeamId == id, cancellationToken);
    }
}
