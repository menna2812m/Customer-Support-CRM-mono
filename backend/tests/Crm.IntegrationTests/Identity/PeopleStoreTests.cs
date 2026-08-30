using Crm.Application.Abstractions;
using Crm.Domain.Identity;
using Crm.Domain.Organization;
using Crm.Infrastructure.Identity;
using Crm.Infrastructure.Persistence;
using Crm.Infrastructure.Persistence.Configurations;
using Crm.IntegrationTests.Infrastructure;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Shouldly;

namespace Crm.IntegrationTests.Identity;

/// <summary>
/// The store's rules, tested where they actually live. The union of permissions is a database join
/// and the administrator count is a database read inside a transaction - neither is provable with a
/// mock, and a test that mocked them would be asserting on its own fixture.
/// </summary>
[Collection(DatabaseCollectionDefinition.Name)]
public sealed class PeopleStoreTests(SqlServerFixture database)
{
    private const string Provider = "https://tests.local/realms/crm";

    private sealed record Fixture(
        SignInHarness Harness,
        IServiceScope Scope,
        CrmDbContext Context,
        IPeopleStore Store) : IAsyncDisposable
    {
        public async ValueTask DisposeAsync()
        {
            Scope.Dispose();
            await Harness.DisposeAsync();
        }
    }

    private Fixture Open()
    {
        var harness = SignInHarness.Create(database.ConnectionString);
        var scope = harness.Services.CreateScope();

        return new Fixture(
            harness,
            scope,
            scope.ServiceProvider.GetRequiredService<CrmDbContext>(),
            scope.ServiceProvider.GetRequiredService<IPeopleStore>());
    }

    private static async Task<User> AddPersonAsync(CrmDbContext context, bool administrator, string? tag = null)
    {
        tag ??= Guid.NewGuid().ToString("n")[..8];

        var person = User.Provision(
            Provider, $"subject-{tag}", $"{tag}@tests.local", $"Person {tag}", 1, OrganizationPlacement.None);

        context.Users.Add(person);

        if (administrator)
        {
            context.RoleAssignments.Add(new RoleAssignment
            {
                UserId = person.Id,
                RoleId = IdentitySeed.AdministratorRoleId,
                GrantedAt = DateTimeOffset.UtcNow,
            });
        }

        await context.SaveChangesAsync();

        return person;
    }

    // ---- T017: roles are held together and their permissions are unioned ----

    [Fact]
    public async Task A_person_may_hold_several_roles_and_their_permissions_are_unioned()
    {
        await using var fixture = Open();

        var actor = await AddPersonAsync(fixture.Context, administrator: true);
        var person = await AddPersonAsync(fixture.Context, administrator: false);

        await fixture.Store.GrantRoleAsync(actor.Id, person.Id, IdentitySeed.AgentRoleId);
        var result = await fixture.Store.GrantRoleAsync(actor.Id, person.Id, IdentitySeed.ReadOnlyRoleId);

        result.IsSuccess.ShouldBeTrue();
        result.Person!.Roles.Count.ShouldBe(2);

        // The union, not a winner: reports.view comes only from read-only, customers.create only
        // from agent, and holding both means holding both.
        result.Person.EffectivePermissions.ShouldContain("reports.view");
        result.Person.EffectivePermissions.ShouldContain("customers.create");
    }

    [Fact]
    public async Task Granting_a_role_the_person_already_holds_changes_nothing_and_does_not_fail()
    {
        await using var fixture = Open();

        var actor = await AddPersonAsync(fixture.Context, administrator: true);
        var person = await AddPersonAsync(fixture.Context, administrator: false);

        await fixture.Store.GrantRoleAsync(actor.Id, person.Id, IdentitySeed.AgentRoleId);
        var second = await fixture.Store.GrantRoleAsync(actor.Id, person.Id, IdentitySeed.AgentRoleId);

        second.IsSuccess.ShouldBeTrue();
        second.Person!.Roles.Count.ShouldBe(1);
    }

    // ---- T018: the guards, enforced by the store rather than only decided by the rule ----

    [Fact]
    public async Task Revoking_the_last_administrators_role_is_refused()
    {
        await using var fixture = Open();

        // Every seeded administrator is removed first, so the one created here really is the last.
        await ClearAdministratorsAsync(fixture.Context);

        var actor = await AddPersonAsync(fixture.Context, administrator: true);
        var last = await AddPersonAsync(fixture.Context, administrator: true);

        // Actor is an administrator too, so remove them from the count deliberately.
        await RemoveAdministratorAsync(fixture.Context, actor.Id);

        var result = await fixture.Store.RevokeRoleAsync(actor.Id, last.Id, IdentitySeed.AdministratorRoleId);

        result.IsSuccess.ShouldBeFalse();
        result.Refusal.ShouldBe(PersonRefusal.LastAdministrator);

        // Refused means unchanged, not merely reported.
        var stillHeld = await fixture.Context.RoleAssignments
            .AnyAsync(a => a.UserId == last.Id && a.RoleId == IdentitySeed.AdministratorRoleId);

        stillHeld.ShouldBeTrue();
    }

    [Fact]
    public async Task Revoking_your_own_administrator_role_is_refused_even_when_others_remain()
    {
        await using var fixture = Open();

        var actor = await AddPersonAsync(fixture.Context, administrator: true);
        await AddPersonAsync(fixture.Context, administrator: true);

        var result = await fixture.Store.RevokeRoleAsync(actor.Id, actor.Id, IdentitySeed.AdministratorRoleId);

        result.IsSuccess.ShouldBeFalse();
        result.Refusal.ShouldBe(PersonRefusal.SelfDemotion);
    }

    [Fact]
    public async Task Revoking_somebody_elses_role_while_administrators_remain_is_allowed()
    {
        await using var fixture = Open();

        var actor = await AddPersonAsync(fixture.Context, administrator: true);
        var other = await AddPersonAsync(fixture.Context, administrator: true);

        var result = await fixture.Store.RevokeRoleAsync(actor.Id, other.Id, IdentitySeed.AdministratorRoleId);

        result.IsSuccess.ShouldBeTrue();
        result.Person!.Roles.ShouldBeEmpty();
    }

    // ---- Placement, enforced by the store rather than only by the entity ----

    [Fact]
    public async Task A_placement_into_an_inactive_unit_is_refused()
    {
        await using var fixture = Open();

        var actor = await AddPersonAsync(fixture.Context, administrator: true);
        var person = await AddPersonAsync(fixture.Context, administrator: false);

        var department = Department.Create($"قسم {Guid.NewGuid():n}"[..12], $"Dept {Guid.NewGuid():n}"[..12], $"D{Guid.NewGuid():n}"[..8]);
        department.Deactivate();
        fixture.Context.Departments.Add(department);
        await fixture.Context.SaveChangesAsync();

        var result = await fixture.Store.SetPlacementAsync(
            actor.Id, person.Id, new PlacementCommand(null, department.Id, null));

        result.Refusal.ShouldBe(PersonRefusal.UnitInactive);
    }

    [Fact]
    public async Task A_department_that_disagrees_with_the_team_is_refused_by_the_store()
    {
        await using var fixture = Open();

        var actor = await AddPersonAsync(fixture.Context, administrator: true);
        var person = await AddPersonAsync(fixture.Context, administrator: false);

        var tag = Guid.NewGuid().ToString("n")[..6];
        var home = Department.Create($"الأصل {tag}", $"Home {tag}", $"H{tag}");
        var elsewhere = Department.Create($"آخر {tag}", $"Else {tag}", $"E{tag}");
        fixture.Context.Departments.AddRange(home, elsewhere);
        await fixture.Context.SaveChangesAsync();

        var team = Team.Create(home, $"فريق {tag}", $"Team {tag}", $"T{tag}");
        fixture.Context.Teams.Add(team);
        await fixture.Context.SaveChangesAsync();

        var result = await fixture.Store.SetPlacementAsync(
            actor.Id, person.Id, new PlacementCommand(null, elsewhere.Id, team.Id));

        result.Refusal.ShouldBe(PersonRefusal.PlacementMismatch);
    }

    [Fact]
    public async Task Placing_on_a_team_derives_the_department()
    {
        await using var fixture = Open();

        var actor = await AddPersonAsync(fixture.Context, administrator: true);
        var person = await AddPersonAsync(fixture.Context, administrator: false);

        var tag = Guid.NewGuid().ToString("n")[..6];
        var department = Department.Create($"قسم {tag}", $"Dept {tag}", $"D{tag}");
        fixture.Context.Departments.Add(department);
        await fixture.Context.SaveChangesAsync();

        var team = Team.Create(department, $"فريق {tag}", $"Team {tag}", $"T{tag}");
        fixture.Context.Teams.Add(team);
        await fixture.Context.SaveChangesAsync();

        var result = await fixture.Store.SetPlacementAsync(
            actor.Id, person.Id, new PlacementCommand(null, null, team.Id));

        result.IsSuccess.ShouldBeTrue();
        result.Person!.Summary.Placement.TeamId.ShouldBe(team.Id);
        result.Person.Summary.Placement.DepartmentId.ShouldBe(department.Id);

        // Both names travel with it, so a list can show either language without a second call.
        result.Person.Summary.Placement.DepartmentNameAr.ShouldBe($"قسم {tag}");
    }

    private static async Task ClearAdministratorsAsync(CrmDbContext context)
    {
        var seeded = await context.RoleAssignments
            .Where(assignment => assignment.RoleId == IdentitySeed.AdministratorRoleId)
            .ToListAsync();

        context.RoleAssignments.RemoveRange(seeded);
        await context.SaveChangesAsync();
    }

    private static async Task RemoveAdministratorAsync(CrmDbContext context, Guid userId)
    {
        var assignment = await context.RoleAssignments
            .FirstOrDefaultAsync(a => a.UserId == userId && a.RoleId == IdentitySeed.AdministratorRoleId);

        if (assignment is not null)
        {
            context.RoleAssignments.Remove(assignment);
            await context.SaveChangesAsync();
        }
    }
}
