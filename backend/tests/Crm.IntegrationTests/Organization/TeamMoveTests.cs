using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using Crm.IntegrationTests.Infrastructure;
using Microsoft.EntityFrameworkCore;
using Shouldly;
using static Crm.IntegrationTests.Organization.OrganizationHarness;

namespace Crm.IntegrationTests.Organization;

/// <summary>
/// User Story 3: reorganizing without corrupting placement (spec FR-014 to FR-017, SC-003).
///
/// This is the feature's one real invariant and the thing it is most likely to get wrong. A team
/// that moves without its members leaves everybody recorded in a department they have left, and
/// nothing in the interface would show it.
/// </summary>
[Collection(DatabaseCollectionDefinition.Name)]
public sealed class TeamMoveTests(SqlServerFixture database)
{
    [Fact]
    public async Task Moving_a_team_carries_its_members_into_the_new_department()
    {
        var tag = Tag();
        var (harness, email) = Administrator(database.ConnectionString);
        await using var _ = harness;
        using var client = await SignInAsync(harness, email);

        var origin = await CreateAsync(client, Departments, $"أصل {tag}", $"Origin {tag}", $"OR{tag}");
        var destination = await CreateAsync(client, Departments, $"وجهة {tag}", $"Target {tag}", $"DE{tag}");

        var team = await ReadIdAsync(await client.PostAsJsonAsync(
            Route($"{Departments}/{origin}/teams"),
            NewUnit($"فريق {tag}", $"Crew {tag}", $"CR{tag}")));

        // Nobody can be placed on a team until feature 004, so the placement is seeded directly -
        // exactly as quickstart.md describes for verifying this by hand.
        await PlaceEveryoneOnAsync(database, team, origin);

        var response = await client.PutAsJsonAsync(
            Route($"{Teams}/{team}/department"),
            Destination(destination));

        response.StatusCode.ShouldBe(HttpStatusCode.OK);

        using var document = JsonDocument.Parse(await response.Content.ReadAsStringAsync());
        document.RootElement.GetProperty("membersReassigned").GetInt32().ShouldBeGreaterThan(0);

        await using var verify = database.CreateContext();

        var stranded = await verify.Users
            .Where(user => user.TeamId == team && user.DepartmentId != destination)
            .CountAsync();

        stranded.ShouldBe(0);
    }

    [Fact]
    public async Task No_user_is_ever_left_in_a_department_their_team_has_left()
    {
        // SC-003 stated as a scan rather than as a spot check: after any move, zero users may have a
        // department that disagrees with their team's. This is INV-2, and it is the assertion that
        // would catch a partial move however it happened.
        var tag = Tag();
        var (harness, email) = Administrator(database.ConnectionString);
        await using var _ = harness;
        using var client = await SignInAsync(harness, email);

        var first = await CreateAsync(client, Departments, $"واحد {tag}", $"One {tag}", $"O1{tag}");
        var second = await CreateAsync(client, Departments, $"اثنان {tag}", $"Two {tag}", $"O2{tag}");
        var third = await CreateAsync(client, Departments, $"ثلاثة {tag}", $"Three {tag}", $"O3{tag}");

        var team = await ReadIdAsync(await client.PostAsJsonAsync(
            Route($"{Departments}/{first}/teams"),
            NewUnit($"متنقل {tag}", $"Rover {tag}", $"RV{tag}")));

        await PlaceEveryoneOnAsync(database, team, first);

        // Move twice, because a resync that only works from the original department would pass a
        // single-move test and fail the second one.
        (await client.PutAsJsonAsync(Route($"{Teams}/{team}/department"), Destination(second)))
            .EnsureSuccessStatusCode();

        (await client.PutAsJsonAsync(Route($"{Teams}/{team}/department"), Destination(third)))
            .EnsureSuccessStatusCode();

        await using var verify = database.CreateContext();

        var violations = await verify.Users
            .Where(user => user.TeamId != null)
            .Join(
                verify.Teams,
                user => user.TeamId,
                t => t.Id,
                (user, t) => new { user.Id, UserDepartment = user.DepartmentId, TeamDepartment = t.DepartmentId })
            .Where(row => row.UserDepartment != row.TeamDepartment)
            .CountAsync();

        violations.ShouldBe(0);
    }

    [Fact]
    public async Task A_move_into_an_inactive_department_is_refused()
    {
        var tag = Tag();
        var (harness, email) = Administrator(database.ConnectionString);
        await using var _ = harness;
        using var client = await SignInAsync(harness, email);

        var origin = await CreateAsync(client, Departments, $"نشط {tag}", $"Live {tag}", $"LV{tag}");
        var closed = await CreateAsync(client, Departments, $"مغلق {tag}", $"Shut {tag}", $"SH{tag}");

        var team = await ReadIdAsync(await client.PostAsJsonAsync(
            Route($"{Departments}/{origin}/teams"),
            NewUnit($"فريق {tag}", $"Unit {tag}", $"UN{tag}")));

        await client.PutAsJsonAsync(Route($"{Departments}/{closed}/activation"), Activation(false));

        var response = await client.PutAsJsonAsync(
            Route($"{Teams}/{team}/department"),
            Destination(closed));

        response.StatusCode.ShouldBe(HttpStatusCode.Conflict);
        (await ReadCodeAsync(response)).ShouldBe("organization_department_inactive");
    }

    [Fact]
    public async Task A_move_into_a_department_holding_that_name_is_refused()
    {
        var tag = Tag();
        var (harness, email) = Administrator(database.ConnectionString);
        await using var _ = harness;
        using var client = await SignInAsync(harness, email);

        var origin = await CreateAsync(client, Departments, $"من {tag}", $"From {tag}", $"FR{tag}");
        var destination = await CreateAsync(client, Departments, $"إلى {tag}", $"To {tag}", $"TO{tag}");

        var team = await ReadIdAsync(await client.PostAsJsonAsync(
            Route($"{Departments}/{origin}/teams"),
            NewUnit($"مستوى أول {tag}", $"Tier One {tag}", $"T1{tag}")));

        // The destination already has a team of this name, so the move would break the
        // per-department uniqueness it is crossing into.
        await client.PostAsJsonAsync(
            Route($"{Departments}/{destination}/teams"),
            NewUnit($"مستوى أول {tag}", $"Tier One {tag}", $"T2{tag}"));

        var response = await client.PutAsJsonAsync(
            Route($"{Teams}/{team}/department"),
            Destination(destination));

        response.StatusCode.ShouldBe(HttpStatusCode.Conflict);
        (await ReadCodeAsync(response)).ShouldBe("organization_name_conflict");
    }

    [Fact]
    public async Task A_move_to_the_department_the_team_is_already_in_succeeds_and_changes_nothing()
    {
        var tag = Tag();
        var (harness, email) = Administrator(database.ConnectionString);
        await using var _ = harness;
        using var client = await SignInAsync(harness, email);

        var department = await CreateAsync(client, Departments, $"ثابت {tag}", $"Stable {tag}", $"ST{tag}");

        var team = await ReadIdAsync(await client.PostAsJsonAsync(
            Route($"{Departments}/{department}/teams"),
            NewUnit($"فريق {tag}", $"Group {tag}", $"GR{tag}")));

        // Re-submitting a move is not a mistake worth an error, and it must not report members moved.
        var response = await client.PutAsJsonAsync(
            Route($"{Teams}/{team}/department"),
            Destination(department));

        response.StatusCode.ShouldBe(HttpStatusCode.OK);

        using var document = JsonDocument.Parse(await response.Content.ReadAsStringAsync());
        document.RootElement.GetProperty("membersReassigned").GetInt32().ShouldBe(0);
    }

    [Fact]
    public async Task A_refused_move_leaves_the_team_and_its_members_untouched()
    {
        // Atomicity, from the outside: a move that is refused must change nothing at all, rather
        // than moving the team and failing to carry its people.
        var tag = Tag();
        var (harness, email) = Administrator(database.ConnectionString);
        await using var _ = harness;
        using var client = await SignInAsync(harness, email);

        var origin = await CreateAsync(client, Departments, $"بقاء {tag}", $"Stay {tag}", $"SY{tag}");
        var closed = await CreateAsync(client, Departments, $"مقفل {tag}", $"Locked {tag}", $"LK{tag}");

        var team = await ReadIdAsync(await client.PostAsJsonAsync(
            Route($"{Departments}/{origin}/teams"),
            NewUnit($"فريق {tag}", $"Band {tag}", $"BD{tag}")));

        await PlaceEveryoneOnAsync(database, team, origin);
        await client.PutAsJsonAsync(Route($"{Departments}/{closed}/activation"), Activation(false));

        (await client.PutAsJsonAsync(Route($"{Teams}/{team}/department"), Destination(closed)))
            .StatusCode.ShouldBe(HttpStatusCode.Conflict);

        await using var verify = database.CreateContext();

        (await verify.Teams.FirstAsync(t => t.Id == team)).DepartmentId.ShouldBe(origin);
        (await verify.Users.Where(user => user.TeamId == team).CountAsync()).ShouldBeGreaterThan(0);
        (await verify.Users.Where(user => user.TeamId == team && user.DepartmentId != origin).CountAsync())
            .ShouldBe(0);
    }

    /// <summary>
    /// Places every existing user on the team, standing in for the placement screen feature 004 will
    /// add. Writing through a bare context is correct here: this is fixture setup, not the behaviour
    /// under test.
    /// </summary>
    private static async Task PlaceEveryoneOnAsync(SqlServerFixture database, Guid team, Guid department)
    {
        await using var context = database.CreateContext();

        var users = await context.Users.ToListAsync();

        foreach (var user in users)
        {
            user.PlaceOnTeam(team, department);
        }

        await context.SaveChangesAsync();
    }
}
