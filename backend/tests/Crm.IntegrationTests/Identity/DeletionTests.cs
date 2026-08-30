using Crm.Application.Abstractions;
using Crm.Domain.Identity;
using Crm.Infrastructure.Persistence;
using Crm.Infrastructure.Persistence.Configurations;
using Crm.IntegrationTests.Infrastructure;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Shouldly;

namespace Crm.IntegrationTests.Identity;

/// <summary>
/// Spec FR-024 and FR-025: deleting somebody is one operation, and the roles they held survive it.
/// </summary>
/// <remarks>
/// What these tests prove, and what they do not.
///
/// They prove that a refused delete changes nothing at all, and that a successful delete does all
/// four things together - roles revoked, sessions ended, person removed, roles reported back for
/// the audit entry. Those are the outcomes the specification names.
///
/// They do not prove behaviour under an infrastructure failure halfway through. Forcing one would
/// need a seam to inject it, and a seam that exists only for a test is a seam somebody can misuse.
/// That case rests on the explicit serializable transaction the store opens, which is visible in
/// the code rather than asserted here. Saying so is better than a test whose name implies more than
/// it demonstrates.
/// </remarks>
[Collection(DatabaseCollectionDefinition.Name)]
public sealed class DeletionTests(SqlServerFixture database)
{
    private const string Provider = "https://tests.local/realms/crm";

    [Fact]
    public async Task Deleting_somebody_revokes_their_roles_ends_their_sessions_and_reports_what_they_held()
    {
        await using var harness = SignInHarness.Create(database.ConnectionString);
        using var scope = harness.Services.CreateScope();
        var context = scope.ServiceProvider.GetRequiredService<CrmDbContext>();
        var store = scope.ServiceProvider.GetRequiredService<IPeopleStore>();

        var actor = await AddAsync(context, administrator: true);
        var doomed = await AddAsync(context, administrator: false);

        context.RoleAssignments.Add(new RoleAssignment
        {
            UserId = doomed.Id,
            RoleId = IdentitySeed.AgentRoleId,
            GrantedAt = DateTimeOffset.UtcNow,
        });

        context.Sessions.Add(Session.Start(doomed.Id, DateTimeOffset.UtcNow, TimeSpan.FromHours(1), TimeSpan.FromHours(8), "tests", null));
        await context.SaveChangesAsync();

        var result = await store.DeleteAsync(actor.Id, doomed.Id);

        result.Deleted.ShouldBeTrue();

        // Reported back because revoking destroyed the only other record of the grant. Without this
        // the audit entry has nothing to carry, and the history exists nowhere (FR-025).
        result.RolesHeldBeforeDeletion.Select(role => role.Name).ShouldContain("Agent");

        context.ChangeTracker.Clear();

        (await context.RoleAssignments.AnyAsync(a => a.UserId == doomed.Id)).ShouldBeFalse();
        (await context.Users.AnyAsync(user => user.Id == doomed.Id)).ShouldBeFalse();

        var live = await context.Sessions.CountAsync(s => s.UserId == doomed.Id && s.RevokedAt == null);
        live.ShouldBe(0);
    }

    [Fact]
    public async Task A_refused_delete_leaves_the_person_their_roles_and_their_sessions_untouched()
    {
        await using var harness = SignInHarness.Create(database.ConnectionString);
        using var scope = harness.Services.CreateScope();
        var context = scope.ServiceProvider.GetRequiredService<CrmDbContext>();
        var store = scope.ServiceProvider.GetRequiredService<IPeopleStore>();

        var actor = await AddAsync(context, administrator: true);

        context.Sessions.Add(Session.Start(actor.Id, DateTimeOffset.UtcNow, TimeSpan.FromHours(1), TimeSpan.FromHours(8), "tests", null));
        await context.SaveChangesAsync();

        // Deleting yourself is refused whatever else is true.
        var result = await store.DeleteAsync(actor.Id, actor.Id);

        result.Deleted.ShouldBeFalse();
        result.Refusal.ShouldBeOneOf(PersonRefusal.SelfDemotion, PersonRefusal.LastAdministrator);

        // Nothing reported, because nothing was revoked.
        result.RolesHeldBeforeDeletion.ShouldBeEmpty();

        context.ChangeTracker.Clear();

        // A refusal that had already ended a session or revoked a role would be worse than no
        // refusal: the person would still exist, holding less than they did.
        (await context.Users.AnyAsync(user => user.Id == actor.Id)).ShouldBeTrue();
        (await context.RoleAssignments.AnyAsync(a => a.UserId == actor.Id)).ShouldBeTrue();
        (await context.Sessions.CountAsync(s => s.UserId == actor.Id && s.RevokedAt == null)).ShouldBe(1);
    }

    [Fact]
    public async Task A_restored_person_holds_no_roles()
    {
        await using var harness = SignInHarness.Create(database.ConnectionString);
        using var scope = harness.Services.CreateScope();
        var context = scope.ServiceProvider.GetRequiredService<CrmDbContext>();
        var store = scope.ServiceProvider.GetRequiredService<IPeopleStore>();

        var actor = await AddAsync(context, administrator: true);
        var person = await AddAsync(context, administrator: false);

        context.RoleAssignments.Add(new RoleAssignment
        {
            UserId = person.Id,
            RoleId = IdentitySeed.AgentRoleId,
            GrantedAt = DateTimeOffset.UtcNow,
        });

        await context.SaveChangesAsync();
        await store.DeleteAsync(actor.Id, person.Id);

        context.ChangeTracker.Clear();

        // Undelete by hand, as a restore feature would.
        var restored = await context.Users.IgnoreQueryFilters().SingleAsync(user => user.Id == person.Id);
        restored.IsDeleted = false;
        await context.SaveChangesAsync();

        // Access is re-granted deliberately, never resurrected (spec FR-027). The audit entry says
        // what they held; it is not an undo buffer.
        (await context.RoleAssignments.AnyAsync(a => a.UserId == person.Id)).ShouldBeFalse();
    }

    private static async Task<User> AddAsync(CrmDbContext context, bool administrator)
    {
        var tag = Guid.NewGuid().ToString("n")[..8];

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
}
