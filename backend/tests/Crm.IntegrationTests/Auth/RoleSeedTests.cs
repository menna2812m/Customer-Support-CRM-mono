using Crm.Application.Authorization;
using Crm.IntegrationTests.Infrastructure;
using Microsoft.EntityFrameworkCore;
using Shouldly;

namespace Crm.IntegrationTests.Auth;

/// <summary>
/// Spec FR-020 and FR-022: role definitions are seeded by migration, and every permission they
/// grant exists in the catalog. A role referencing a permission that does not exist would fail
/// silently at authorization time - the caller would simply never be admitted, with nothing to
/// explain why.
/// </summary>
[Collection(DatabaseCollectionDefinition.Name)]
public sealed class RoleSeedTests(SqlServerFixture database)
{
    [Fact]
    public async Task The_three_system_roles_are_seeded()
    {
        await using var context = database.CreateContext();

        var roles = await context.Roles.OrderBy(role => role.Name).ToListAsync();

        roles.Select(role => role.Name).ShouldBe(["Administrator", "Agent", "ReadOnly"]);
        roles.ShouldAllBe(role => role.IsSystem);
    }

    [Fact]
    public async Task The_administrator_role_holds_every_permission_in_the_catalog()
    {
        await using var context = database.CreateContext();

        var administrator = await context.Roles.SingleAsync(role => role.Name == "Administrator");

        var granted = await context.RolePermissions
            .Where(grant => grant.RoleId == administrator.Id)
            .Select(grant => grant.Permission)
            .ToListAsync();

        // Resolved from the registry at seed time, so a permission added to the catalog later is
        // not quietly missing from the role that is supposed to hold everything.
        granted.Order(StringComparer.Ordinal)
            .ShouldBe(Permissions.All.Order(StringComparer.Ordinal));
    }

    [Fact]
    public async Task Every_seeded_permission_exists_in_the_catalog()
    {
        await using var context = database.CreateContext();

        var granted = await context.RolePermissions
            .Select(grant => grant.Permission)
            .Distinct()
            .ToListAsync();

        var unknown = granted.Where(permission => !Permissions.Exists(permission)).ToList();

        unknown.ShouldBeEmpty(
            "A seeded role grants a permission the catalog does not define: "
                + string.Join(", ", unknown));
    }

    [Fact]
    public async Task The_agent_role_grants_day_to_day_work_but_not_administration()
    {
        await using var context = database.CreateContext();

        var agent = await context.Roles.SingleAsync(role => role.Name == "Agent");

        var granted = await context.RolePermissions
            .Where(grant => grant.RoleId == agent.Id)
            .Select(grant => grant.Permission)
            .ToListAsync();

        granted.ShouldContain(Permissions.Tickets.Create);

        // The distinction that matters: an agent works tickets, an administrator manages people.
        granted.ShouldNotContain(Permissions.Users.Manage);
    }
}
