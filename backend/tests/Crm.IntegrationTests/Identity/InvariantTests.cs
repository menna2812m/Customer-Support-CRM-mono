using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using Crm.IntegrationTests.Infrastructure;
using Microsoft.EntityFrameworkCore;
using Shouldly;
using static Crm.IntegrationTests.Organization.OrganizationHarness;

namespace Crm.IntegrationTests.Identity;

/// <summary>
/// INV-2 and SC-002 stated as a scan: no person's department may disagree with their team's, at any
/// time and after any sequence of placements, team moves, and deletions.
/// </summary>
/// <remarks>
/// Feature 003 proved this for the half it owned - moving a team resyncs its members. This proves
/// the half feature 004 added, which is the harder one: placement is now something an administrator
/// changes directly, repeatedly, and in combination with the moves feature 003 already allowed. A
/// spot check would pass on any single one of those operations. The failure this guards against is
/// two correct operations composing into a wrong state.
///
/// Deleted people are outside the scan on purpose. Their placement is frozen at the moment they
/// were removed and nothing reads it; restoring somebody returns them inactive and unplaced by
/// their team's current shape, which is FR-027's business rather than this invariant's.
/// </remarks>
[Collection(DatabaseCollectionDefinition.Name)]
public sealed class InvariantTests(SqlServerFixture database)
{
    private const string People = "/api/v1/identity/people";

    [Fact]
    public async Task No_person_is_left_in_a_department_their_team_has_left()
    {
        var tag = Tag();
        var (harness, email) = Administrator(database.ConnectionString);
        await using var _ = harness;
        using var client = await SignInAsync(harness, email);

        var branch = await CreateAsync(client, Branches, $"فرع {tag}", $"Branch {tag}", $"BR{tag}");
        var origin = await CreateAsync(client, Departments, $"مصدر {tag}", $"Origin {tag}", $"OR{tag}");
        var destination = await CreateAsync(client, Departments, $"وجهة {tag}", $"Target {tag}", $"TG{tag}");

        var team = await ReadIdAsync(await client.PostAsJsonAsync(
            Route($"{Departments}/{origin}/teams"),
            NewUnit($"فريق {tag}", $"Team {tag}", $"TM{tag}")));

        var onTheTeam = await PrepareAsync(client, tag, "On The Team");
        var movedOnLater = await PrepareAsync(client, tag, "Moved On Later");
        var departing = await PrepareAsync(client, tag, "Departing");

        // A placement by team, a placement by department, and a placement by branch alone - the
        // three shapes the screen can produce, so the scan is over all of them rather than one.
        await PlaceAsync(client, onTheTeam, new { branchId = branch, teamId = team });
        await PlaceAsync(client, movedOnLater, new { branchId = branch, departmentId = origin });
        await PlaceAsync(client, departing, new { branchId = branch });

        // Move the team out from under somebody who is on it (spec FR-015 of feature 003).
        (await client.PutAsJsonAsync(Route($"{Teams}/{team}/department"), Destination(destination)))
            .EnsureSuccessStatusCode();

        // Then place a second person onto it, now that it lives somewhere else. A derivation that
        // read the team's original department would go wrong exactly here.
        await PlaceAsync(client, movedOnLater, new { branchId = branch, teamId = team });

        // And clear a placement entirely, which is how somebody leaves a unit.
        await PlaceAsync(client, departing, new { });

        (await client.DeleteAsync(Route($"{People}/{departing}"))).StatusCode
            .ShouldBe(HttpStatusCode.NoContent);

        await using var verify = database.CreateContext();

        var violations = await verify.Users
            .Where(user => user.TeamId != null)
            .Join(
                verify.Teams,
                user => user.TeamId,
                team => team.Id,
                (user, team) => new { user.Id, Held = user.DepartmentId, Actual = team.DepartmentId })
            .Where(row => row.Held != row.Actual)
            .CountAsync();

        violations.ShouldBe(0);
    }

    [Fact]
    public async Task Clearing_a_team_leaves_no_department_behind_that_nothing_chose()
    {
        var tag = Tag();
        var (harness, email) = Administrator(database.ConnectionString);
        await using var _ = harness;
        using var client = await SignInAsync(harness, email);

        var department = await CreateAsync(client, Departments, $"قسم {tag}", $"Department {tag}", $"DP{tag}");

        var team = await ReadIdAsync(await client.PostAsJsonAsync(
            Route($"{Departments}/{department}/teams"),
            NewUnit($"فريق {tag}", $"Team {tag}", $"TM{tag}")));

        var person = await PrepareAsync(client, tag, "Reassigned");

        await PlaceAsync(client, person, new { teamId = team });

        // Clearing the team clears the department it derived. Leaving the department behind would
        // be a value nobody chose - it was only ever a consequence of the team.
        await PlaceAsync(client, person, new { });

        var detail = await client.GetAsync(Route($"{People}/{person}"));
        using var document = JsonDocument.Parse(await detail.Content.ReadAsStringAsync());

        var placement = document.RootElement.GetProperty("summary").GetProperty("placement");

        // Absent rather than null: the API omits null properties, so "cleared" reaches the client
        // as a missing key. Asserting on either shape alone would pass for the wrong reason, which
        // is why this reads the property rather than its value.
        Cleared(placement, "teamId");
        Cleared(placement, "departmentId");
    }

    private static void Cleared(JsonElement placement, string property)
    {
        var present = placement.TryGetProperty(property, out var value);
        var cleared = !present || value.ValueKind == JsonValueKind.Null;

        cleared.ShouldBeTrue($"{property} should have been cleared, but carried {value}");
    }

    private static async Task<Guid> PrepareAsync(HttpClient client, string tag, string name)
    {
        var response = await client.PostAsJsonAsync(
            Route(People),
            new { email = $"{Tag()}-{tag}@prepared.local", displayName = name });

        response.EnsureSuccessStatusCode();

        using var document = JsonDocument.Parse(await response.Content.ReadAsStringAsync());

        return document.RootElement.GetProperty("summary").GetProperty("id").GetGuid();
    }

    private static async Task PlaceAsync(HttpClient client, Guid personId, object placement)
    {
        var response = await client.PutAsJsonAsync(Route($"{People}/{personId}/placement"), placement);

        response.EnsureSuccessStatusCode();
    }
}
