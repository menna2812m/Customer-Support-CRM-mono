using Crm.Application.Abstractions;
using Crm.Application.Common;
using Crm.Domain.Organization;

namespace Crm.Application.Organization;

/// <summary>
/// Maintaining branches and departments. The two behave identically - only teams have a containment
/// rule - so they share one service rather than two copies of the same six operations.
/// </summary>
/// <remarks>
/// Every mutation is audited (AR-005). Auditing lives here rather than in the controller because
/// Constitution I keeps rules out of HTTP concerns, and "this change is worth recording" is a rule.
/// </remarks>
public sealed class OrganizationUnitService(
    IOrganizationStore store,
    IAuditRecorder audit,
    ICurrentUser currentUser,
    ICorrelationAccessor correlation,
    TimeProvider clock)
{
    public Task<PagedResult<OrganizationUnitRecord>> ListAsync<TUnit>(
        UnitListQuery query,
        CancellationToken cancellationToken = default)
        where TUnit : OrganizationUnit =>
        store.ListAsync<TUnit>(query, cancellationToken);

    public Task<OrganizationUnitRecord?> FindAsync<TUnit>(
        Guid id,
        CancellationToken cancellationToken = default)
        where TUnit : OrganizationUnit =>
        store.FindAsync<TUnit>(id, cancellationToken);

    public async Task<OrganizationOutcome<OrganizationUnitRecord>> CreateAsync<TUnit>(
        Func<string, string, string, TUnit> create,
        string nameAr,
        string nameEn,
        string code,
        CancellationToken cancellationToken = default)
        where TUnit : OrganizationUnit
    {
        ArgumentNullException.ThrowIfNull(create);

        if (await store.CodeExistsAsync<TUnit>(code, cancellationToken))
        {
            return OrganizationOutcome.Refused<OrganizationUnitRecord>(
                OrganizationRefusal.CodeConflict,
                OrganizationUnit.Normalize(code));
        }

        if (await store.NameExistsAsync<TUnit>(nameAr, nameEn, null, cancellationToken))
        {
            return OrganizationOutcome.Refused<OrganizationUnitRecord>(
                OrganizationRefusal.NameConflict);
        }

        var record = await store.AddAsync(create(nameAr, nameEn, code), cancellationToken);
        await RecordAsync<TUnit>("created", record.Id, cancellationToken);

        return OrganizationOutcome.Success(record);
    }

    public async Task<OrganizationOutcome<OrganizationUnitRecord>> RenameAsync<TUnit>(
        Guid id,
        string nameAr,
        string nameEn,
        CancellationToken cancellationToken = default)
        where TUnit : OrganizationUnit
    {
        // Excluding this unit, so renaming it to what it is already called is not a conflict.
        if (await store.NameExistsAsync<TUnit>(nameAr, nameEn, id, cancellationToken))
        {
            return OrganizationOutcome.Refused<OrganizationUnitRecord>(
                OrganizationRefusal.NameConflict);
        }

        var record = await store.RenameAsync<TUnit>(id, nameAr, nameEn, cancellationToken);

        if (record is null)
        {
            return OrganizationOutcome.Refused<OrganizationUnitRecord>(OrganizationRefusal.NotFound);
        }

        await RecordAsync<TUnit>("renamed", id, cancellationToken);

        return OrganizationOutcome.Success(record);
    }

    public async Task<OrganizationOutcome<OrganizationUnitRecord>> SetActivationAsync<TUnit>(
        Guid id,
        bool isActive,
        CancellationToken cancellationToken = default)
        where TUnit : OrganizationUnit
    {
        var record = await store.SetActivationAsync<TUnit>(id, isActive, cancellationToken);

        if (record is null)
        {
            return OrganizationOutcome.Refused<OrganizationUnitRecord>(OrganizationRefusal.NotFound);
        }

        await RecordAsync<TUnit>(isActive ? "activated" : "deactivated", id, cancellationToken);

        return OrganizationOutcome.Success(record);
    }

    public async Task<OrganizationOutcome<bool>> DeleteAsync<TUnit>(
        Guid id,
        CancellationToken cancellationToken = default)
        where TUnit : OrganizationUnit
    {
        if (await store.FindAsync<TUnit>(id, cancellationToken) is null)
        {
            return OrganizationOutcome.Refused<bool>(OrganizationRefusal.NotFound);
        }

        var dependents = await store.CountDependentsAsync<TUnit>(id, cancellationToken);

        if (dependents.Any)
        {
            // The refusal names what depends on the unit. A refusal that does not say why leaves an
            // administrator guessing which of several things to fix first (spec FR-012).
            return OrganizationOutcome.Refused<bool>(
                OrganizationRefusal.HasDependents,
                Describe(dependents));
        }

        await store.DeleteAsync<TUnit>(id, cancellationToken);
        await RecordAsync<TUnit>("deleted", id, cancellationToken);

        return OrganizationOutcome.Success(true);
    }

    internal static string Describe(DependentSummary dependents)
    {
        var parts = new List<string>(2);

        if (dependents.Teams > 0)
        {
            parts.Add($"{dependents.Teams} team(s)");
        }

        if (dependents.People > 0)
        {
            parts.Add($"{dependents.People} person(s)");
        }

        return string.Join(" and ", parts);
    }

    private Task RecordAsync<TUnit>(string action, Guid id, CancellationToken cancellationToken)
        where TUnit : OrganizationUnit =>
        audit.RecordAsync(
            new AuditEntry(
                $"organization.{typeof(TUnit).Name.ToLowerInvariant()}.{action}",
                currentUser.UserId,
                typeof(TUnit).Name,
                id.ToString(),
                clock.GetUtcNow(),
                correlation.CorrelationId),
            cancellationToken);
}
