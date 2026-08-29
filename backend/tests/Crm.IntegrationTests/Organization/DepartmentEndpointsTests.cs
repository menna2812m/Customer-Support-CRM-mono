using System.Net;
using System.Net.Http.Json;
using Crm.IntegrationTests.Infrastructure;
using Shouldly;
using static Crm.IntegrationTests.Organization.OrganizationHarness;

namespace Crm.IntegrationTests.Organization;

/// <summary>
/// User Story 1: departments and the teams inside them (spec FR-005, FR-006, FR-008 to FR-013).
///
/// The negative tests carry the weight. Creating a department is not where this feature can go
/// wrong; refusing a duplicate, refusing a delete that would strand teams, and hiding an inactive
/// unit from a placement chooser are.
/// </summary>
[Collection(DatabaseCollectionDefinition.Name)]
public sealed class DepartmentEndpointsTests(SqlServerFixture database)
{
    [Fact]
    public async Task A_department_is_created_and_read_back_in_both_languages()
    {
        var tag = Tag();
        var (harness, email) = Administrator(database.ConnectionString);
        await using var _ = harness;
        using var client = await SignInAsync(harness, email);

        var id = await CreateAsync(client, Departments, $"الدعم الفني {tag}", $"Technical Support {tag}", $"TS{tag}");

        var response = await client.GetAsync(Route($"{Departments}/{id}"));
        response.StatusCode.ShouldBe(HttpStatusCode.OK);

        var body = await response.Content.ReadAsStringAsync();
        body.ShouldContain($"Technical Support {tag}");
        body.ShouldContain($"الدعم الفني {tag}");
    }

    [Fact]
    public async Task A_duplicate_code_is_refused_ignoring_case_and_surrounding_whitespace()
    {
        var tag = Tag();
        var (harness, email) = Administrator(database.ConnectionString);
        await using var _ = harness;
        using var client = await SignInAsync(harness, email);

        await CreateAsync(client, Departments, $"الدعم {tag}", $"Support {tag}", $"DUP{tag}");

        // Differing only by case and padding is the same code (spec FR-006).
        var response = await client.PostAsJsonAsync(
            Route(Departments),
            NewUnit($"مبيعات {tag}", $"Sales {tag}", $"  dup{tag}  "));

        response.StatusCode.ShouldBe(HttpStatusCode.Conflict);
        (await ReadCodeAsync(response)).ShouldBe("organization_code_conflict");
    }

    [Fact]
    public async Task A_duplicate_department_name_is_refused_in_either_language()
    {
        var tag = Tag();
        var (harness, email) = Administrator(database.ConnectionString);
        await using var _ = harness;
        using var client = await SignInAsync(harness, email);

        await CreateAsync(client, Departments, $"الفوترة {tag}", $"Billing {tag}", $"B1{tag}");

        // Each language is checked independently: matching only the English name is still a clash,
        // because the English list would show two entries a reader cannot tell apart.
        var response = await client.PostAsJsonAsync(
            Route(Departments),
            NewUnit($"شيء آخر {tag}", $"Billing {tag}", $"B2{tag}"));

        response.StatusCode.ShouldBe(HttpStatusCode.Conflict);
        (await ReadCodeAsync(response)).ShouldBe("organization_name_conflict");
    }

    [Fact]
    public async Task Renaming_a_department_to_its_own_name_is_not_a_conflict()
    {
        var tag = Tag();
        var (harness, email) = Administrator(database.ConnectionString);
        await using var _ = harness;
        using var client = await SignInAsync(harness, email);

        var id = await CreateAsync(client, Departments, $"الجودة {tag}", $"Quality {tag}", $"Q1{tag}");

        // Excluding itself from the uniqueness check - otherwise correcting only the Arabic name
        // would be impossible, because the English one would collide with itself.
        var response = await client.PutAsJsonAsync(
            Route($"{Departments}/{id}"),
            Rename($"جودة الخدمة {tag}", $"Quality {tag}"));

        response.StatusCode.ShouldBe(HttpStatusCode.OK);
    }

    [Fact]
    public async Task A_rename_cannot_change_the_code()
    {
        var tag = Tag();
        var (harness, email) = Administrator(database.ConnectionString);
        await using var _ = harness;
        using var client = await SignInAsync(harness, email);

        var id = await CreateAsync(client, Departments, $"الشحن {tag}", $"Shipping {tag}", $"SHP{tag}");

        // The code is absent from the rename contract rather than present and ignored. Sending it
        // anyway must leave the stored code alone (spec FR-006).
        await client.PutAsJsonAsync(
            Route($"{Departments}/{id}"),
            new { nameAr = $"الشحن {tag}", nameEn = $"Shipping {tag}", code = "CHANGED" });

        var body = await (await client.GetAsync(Route($"{Departments}/{id}"))).Content.ReadAsStringAsync();

        body.ShouldContain($"SHP{tag}");
        body.ShouldNotContain("CHANGED");
    }

    [Fact]
    public async Task A_department_with_teams_cannot_be_deleted_and_the_refusal_names_them()
    {
        var tag = Tag();
        var (harness, email) = Administrator(database.ConnectionString);
        await using var _ = harness;
        using var client = await SignInAsync(harness, email);

        var department = await CreateAsync(client, Departments, $"العمليات {tag}", $"Operations {tag}", $"OPS{tag}");
        await client.PostAsJsonAsync(
            Route($"{Departments}/{department}/teams"),
            NewUnit($"المستوى الأول {tag}", $"Tier 1 {tag}", $"OPS-T1{tag}"));

        var response = await client.DeleteAsync(Route($"{Departments}/{department}"));

        response.StatusCode.ShouldBe(HttpStatusCode.Conflict);
        (await ReadCodeAsync(response)).ShouldBe("organization_has_dependents");

        // A refusal that does not say what depends on it cannot be acted on (spec FR-012).
        (await ReadDetailAsync(response)).ShouldNotBeNull().ShouldContain("team");
    }

    [Fact]
    public async Task A_department_with_nothing_depending_on_it_is_deleted_and_its_code_freed()
    {
        var tag = Tag();
        var (harness, email) = Administrator(database.ConnectionString);
        await using var _ = harness;
        using var client = await SignInAsync(harness, email);

        var id = await CreateAsync(client, Departments, $"خطأ {tag}", $"Mistake {tag}", $"OOPS{tag}");

        (await client.DeleteAsync(Route($"{Departments}/{id}"))).StatusCode
            .ShouldBe(HttpStatusCode.NoContent);

        (await client.GetAsync(Route($"{Departments}/{id}"))).StatusCode
            .ShouldBe(HttpStatusCode.NotFound);

        // Delete-and-recreate is the stated remedy for a mistyped code, so the code must come back
        // into circulation. This is what the filtered unique indexes exist for.
        var recreated = await client.PostAsJsonAsync(
            Route(Departments),
            NewUnit($"صحيح {tag}", $"Correct {tag}", $"OOPS{tag}"));

        recreated.StatusCode.ShouldBe(HttpStatusCode.Created);
    }

    [Fact]
    public async Task A_deactivated_department_stays_in_administration_but_leaves_the_active_listing()
    {
        var tag = Tag();
        var (harness, email) = Administrator(database.ConnectionString);
        await using var _ = harness;
        using var client = await SignInAsync(harness, email);

        var id = await CreateAsync(client, Departments, $"مغلق {tag}", $"Closed {tag}", $"CLS{tag}");

        await client.PutAsJsonAsync(Route($"{Departments}/{id}/activation"), Activation(false));

        var all = await client.GetAsync(Route($"{Departments}?pageSize=100"));
        var active = await client.GetAsync(Route($"{Departments}?pageSize=100&activeOnly=true"));

        (await all.Content.ReadAsStringAsync()).ShouldContain($"Closed {tag}");

        // activeOnly exists so a placement chooser never has to filter for itself (spec FR-009).
        (await active.Content.ReadAsStringAsync()).ShouldNotContain($"Closed {tag}");
    }

    [Fact]
    public async Task A_team_name_may_repeat_across_departments_but_not_within_one()
    {
        var tag = Tag();
        var (harness, email) = Administrator(database.ConnectionString);
        await using var _ = harness;
        using var client = await SignInAsync(harness, email);

        var support = await CreateAsync(client, Departments, $"دعم {tag}", $"Support {tag}", $"SUP{tag}");
        var billing = await CreateAsync(client, Departments, $"فوترة {tag}", $"Finance {tag}", $"FIN{tag}");

        (await client.PostAsJsonAsync(
            Route($"{Departments}/{support}/teams"),
            NewUnit($"المستوى الأول {tag}", $"Tier 1 {tag}", $"SUP-T1{tag}"))).StatusCode
            .ShouldBe(HttpStatusCode.Created);

        // The same name under a different department is normal: a team name is only ever read
        // under its department, so $"Tier 1 {tag}" may exist under several.
        (await client.PostAsJsonAsync(
            Route($"{Departments}/{billing}/teams"),
            NewUnit($"المستوى الأول {tag}", $"Tier 1 {tag}", $"FIN-T1{tag}"))).StatusCode
            .ShouldBe(HttpStatusCode.Created);

        // Within one department it is a clash.
        var clash = await client.PostAsJsonAsync(
            Route($"{Departments}/{support}/teams"),
            NewUnit($"المستوى الأول {tag}", $"Tier 1 {tag}", $"SUP-T1-B{tag}"));

        clash.StatusCode.ShouldBe(HttpStatusCode.Conflict);
        (await ReadCodeAsync(clash)).ShouldBe("organization_name_conflict");
    }

    [Fact]
    public async Task A_team_code_is_unique_across_every_department()
    {
        var tag = Tag();
        var (harness, email) = Administrator(database.ConnectionString);
        await using var _ = harness;
        using var client = await SignInAsync(harness, email);

        var first = await CreateAsync(client, Departments, $"أول {tag}", $"First {tag}", $"D1{tag}");
        var second = await CreateAsync(client, Departments, $"ثاني {tag}", $"Second {tag}", $"D2{tag}");

        await client.PostAsJsonAsync(
            Route($"{Departments}/{first}/teams"),
            NewUnit($"فريق {tag}", $"Team {tag}", $"SHARED{tag}"));

        // Unlike names, a code identifies a team globally - the asymmetry is deliberate.
        var clash = await client.PostAsJsonAsync(
            Route($"{Departments}/{second}/teams"),
            NewUnit($"فريق آخر {tag}", $"Other Team {tag}", $"SHARED{tag}"));

        clash.StatusCode.ShouldBe(HttpStatusCode.Conflict);
        (await ReadCodeAsync(clash)).ShouldBe("organization_code_conflict");
    }

    [Fact]
    public async Task Teams_are_listed_under_their_own_department_and_no_other()
    {
        var tag = Tag();
        var (harness, email) = Administrator(database.ConnectionString);
        await using var _ = harness;
        using var client = await SignInAsync(harness, email);

        var mine = await CreateAsync(client, Departments, $"لي {tag}", $"Mine {tag}", $"MINE{tag}");
        var other = await CreateAsync(client, Departments, $"آخر {tag}", $"Other {tag}", $"OTHR{tag}");

        await client.PostAsJsonAsync(
            Route($"{Departments}/{mine}/teams"),
            NewUnit($"فريقي {tag}", $"My Team {tag}", $"MINE-T{tag}"));

        (await ReadTotalAsync(await client.GetAsync(Route($"{Departments}/{mine}/teams")))).ShouldBe(1);
        (await ReadTotalAsync(await client.GetAsync(Route($"{Departments}/{other}/teams")))).ShouldBe(0);
    }

    [Fact]
    public async Task Creating_a_team_in_a_department_that_does_not_exist_is_a_not_found()
    {
        var tag = Tag();
        var (harness, email) = Administrator(database.ConnectionString);
        await using var _ = harness;
        using var client = await SignInAsync(harness, email);

        var response = await client.PostAsJsonAsync(
            Route($"{Departments}/{Guid.CreateVersion7()}/teams"),
            NewUnit($"فريق {tag}", $"Team {tag}", $"GHOST{tag}"));

        response.StatusCode.ShouldBe(HttpStatusCode.NotFound);
    }
}
