using Crm.Application.Abstractions;
using Crm.Application.Identity;
using Crm.Domain.Identity;
using Crm.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;

namespace Crm.Infrastructure.Identity;

/// <summary>
/// The CRM's user, role, and assignment records (spec FR-004, FR-020, FR-021).
///
/// Every lookup that identifies a person goes through the provider subject. The email lookup exists
/// only to detect a collision before provisioning - it never authenticates anybody.
/// </summary>
public sealed class IdentityStore(
    CrmDbContext context,
    TimeProvider clock,
    ILogger<IdentityStore> logger) : IIdentityStore
{
    public async Task<UserRecord?> FindBySubjectAsync(
        string providerSubject,
        CancellationToken cancellationToken = default)
    {
        var user = await context.Users
            .AsNoTracking()
            .FirstOrDefaultAsync(entry => entry.ProviderSubject == providerSubject, cancellationToken);

        return ToRecord(user);
    }

    public async Task<UserRecord?> FindByEmailAsync(string email, CancellationToken cancellationToken = default)
    {
        var normalized = User.NormalizeEmail(email);

        var user = await context.Users
            .AsNoTracking()
            .FirstOrDefaultAsync(entry => entry.Email == normalized, cancellationToken);

        return ToRecord(user);
    }

    public async Task<UserRecord?> FindByIdAsync(Guid userId, CancellationToken cancellationToken = default)
    {
        var user = await context.Users
            .AsNoTracking()
            .FirstOrDefaultAsync(entry => entry.Id == userId, cancellationToken);

        return ToRecord(user);
    }

    public async Task<UserRecord> ProvisionAsync(
        string providerSubject,
        string email,
        string displayName,
        CancellationToken cancellationToken = default)
    {
        // A new user has no placement. An administrator assigns it afterwards (spec FR-018).
        var user = User.Provision(
            providerSubject,
            email,
            displayName,
            (int)CallerPopulation.Staff,
            OrganizationPlacement.None);

        user.RecordSignIn(clock.GetUtcNow());

        context.Users.Add(user);
        await context.SaveChangesAsync(cancellationToken);

        return ToRecord(user)!;
    }

    public async Task RefreshAsync(
        Guid userId,
        string email,
        string displayName,
        CancellationToken cancellationToken = default)
    {
        var user = await context.Users.FirstOrDefaultAsync(entry => entry.Id == userId, cancellationToken);

        if (user is null)
        {
            return;
        }

        user.RefreshFromProvider(email, displayName);

        user.RecordSignIn(clock.GetUtcNow());

        await context.SaveChangesAsync(cancellationToken);
    }

    public async Task<IReadOnlySet<string>> GetEffectivePermissionsAsync(
        Guid userId,
        CancellationToken cancellationToken = default)
    {
        // Fetched here, resolved in the Application layer: the union and the catalog check are
        // rules, and rules do not live in the store (Constitution I). Computed at sign-in and at
        // each renewal, which is what bounds staleness to one renewal cycle (spec FR-025).
        var granted = await context.RoleAssignments
            .Where(assignment => assignment.UserId == userId)
            .Join(
                context.RolePermissions,
                assignment => assignment.RoleId,
                grant => grant.RoleId,
                (_, grant) => grant.Permission)
            .Distinct()
            .ToListAsync(cancellationToken);

        var resolved = EffectivePermissions.Resolve(granted);

        if (resolved.Unknown.Count > 0)
        {
            // Granted nothing, and said so. Silence here is how a renamed permission becomes an
            // unexplained loss of access weeks later.
            logger.LogError(
                "User {UserId} holds {UnknownCount} role permission(s) the catalog does not declare: {Unknown}",
                userId,
                resolved.Unknown.Count,
                string.Join(", ", resolved.Unknown.Order(StringComparer.Ordinal)));
        }

        return resolved.Permissions;
    }

    public async Task<bool> HasAnyRoleAsync(Guid userId, CancellationToken cancellationToken = default) =>
        await context.RoleAssignments.AnyAsync(assignment => assignment.UserId == userId, cancellationToken);

    public async Task<bool> GrantRoleAsync(
        Guid userId,
        string roleName,
        CancellationToken cancellationToken = default)
    {
        var role = await context.Roles
            .AsNoTracking()
            .FirstOrDefaultAsync(entry => entry.Name == roleName, cancellationToken);

        if (role is null)
        {
            return false;
        }

        var alreadyHeld = await context.RoleAssignments
            .AnyAsync(assignment => assignment.UserId == userId && assignment.RoleId == role.Id, cancellationToken);

        if (alreadyHeld)
        {
            return true;
        }

        context.RoleAssignments.Add(new RoleAssignment
        {
            UserId = userId,
            RoleId = role.Id,
            GrantedAt = clock.GetUtcNow(),

            // Null actor: granted by the deployment's configuration rather than by a person. The
            // audit record says which rule applied.
            GrantedBy = null,
        });

        await context.SaveChangesAsync(cancellationToken);

        return true;
    }

    public async Task<bool> DeactivateAsync(Guid userId, CancellationToken cancellationToken = default)
    {
        var user = await context.Users.FirstOrDefaultAsync(entry => entry.Id == userId, cancellationToken);

        if (user is null || !user.IsActive)
        {
            return false;
        }

        user.Deactivate();
        await context.SaveChangesAsync(cancellationToken);

        return true;
    }

    public async Task<IReadOnlyList<RolePermissionGrant>> GetRolePermissionsAsync(
        CancellationToken cancellationToken = default) =>
        await context.RolePermissions
            .AsNoTracking()
            .Join(
                context.Roles,
                grant => grant.RoleId,
                role => role.Id,
                (grant, role) => new RolePermissionGrant(role.Name, grant.Permission))
            .ToListAsync(cancellationToken);

    private static UserRecord? ToRecord(User? user) =>
        user is null
            ? null
            : new UserRecord(
                user.Id,
                user.ProviderSubject,
                user.Email,
                user.DisplayName,
                user.IsActive,
                user.DepartmentId is null && user.BranchId is null && user.TeamId is null
                    ? null
                    : new OrganizationScope(user.DepartmentId, user.BranchId, user.TeamId));
}
