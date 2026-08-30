using Crm.Application.Abstractions;
using Crm.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;

namespace Crm.Infrastructure.Identity;

/// <summary>Reads the seeded roles and the permissions behind them.</summary>
public sealed class RoleCatalog(CrmDbContext context) : IRoleCatalog
{
    public async Task<IReadOnlyList<RoleDetail>> ListAsync(CancellationToken cancellationToken = default)
    {
        var roles = await context.Roles
            .AsNoTracking()
            .OrderBy(role => role.Name)
            .Select(role => new { role.Id, role.Name })
            .ToListAsync(cancellationToken);

        // One query for every grant rather than one per role: three roles today, and the shape
        // should not become a reason to avoid adding a fourth.
        var grants = await context.RolePermissions
            .AsNoTracking()
            .Select(grant => new { grant.RoleId, grant.Permission })
            .ToListAsync(cancellationToken);

        var byRole = grants
            .GroupBy(grant => grant.RoleId)
            .ToDictionary(
                group => group.Key,
                group => (IReadOnlyList<string>)[.. group.Select(g => g.Permission).OrderBy(p => p, StringComparer.Ordinal)]);

        return
        [
            .. roles.Select(role => new RoleDetail(
                role.Id,
                role.Name,
                byRole.TryGetValue(role.Id, out var permissions) ? permissions : []))
        ];
    }
}
