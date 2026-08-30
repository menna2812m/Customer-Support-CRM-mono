using System.Net.Http.Json;
using Crm.Domain.Organization;
using Crm.IntegrationTests.Infrastructure;
using Microsoft.EntityFrameworkCore;
using Shouldly;

namespace Crm.IntegrationTests.Organization;

/// <summary>
/// What the schema itself guarantees, independently of any endpoint: uniqueness that survives soft
/// deletion, and uniqueness enforced by the database rather than only by a prior read.
/// </summary>
[Collection(DatabaseCollectionDefinition.Name)]
public sealed class SchemaTests(SqlServerFixture database)
{
    [Fact]
    public async Task The_code_columns_compare_case_insensitively()
    {
        // Feature 003's research assumed the database's default collation is case-insensitive, and
        // built code comparison on that assumption. An assumption a whole rule rests on is worth
        // asserting rather than believing - if a deployment ever used a case-sensitive collation,
        // "DUP" and "dup" would become different codes and the rule would silently weaken.
        await using var context = database.CreateContext();

        var collation = await context.Database
            .SqlQuery<string>($"SELECT CONVERT(nvarchar(128), DATABASEPROPERTYEX(DB_NAME(), 'Collation')) AS Value")
            .FirstAsync();

        collation.ShouldContain("_CI_", Case.Insensitive);
    }

    [Fact]
    public async Task Deleting_through_the_application_retires_the_row_rather_than_removing_it()
    {
        // Deliberately routed through the API rather than through the fixture's own context. The
        // conversion of a delete into a retirement lives in AuditingSaveChangesInterceptor, which is
        // registered by the application's DI - a bare context built for a test has no interceptors,
        // so Remove() there is a genuine hard delete and would prove the opposite of the rule.
        var tag = OrganizationHarness.Tag();
        var (harness, email) = OrganizationHarness.Administrator(database.ConnectionString);
        await using var _ = harness;
        using var client = await OrganizationHarness.SignInAsync(harness, email);

        var id = await OrganizationHarness.CreateAsync(
            client,
            OrganizationHarness.Branches,
            $"مخفي {tag}",
            $"Hidden {tag}",
            $"GONE{tag}");

        var deleted = await client.DeleteAsync(
            OrganizationHarness.Route($"{OrganizationHarness.Branches}/{id}"));

        deleted.EnsureSuccessStatusCode();

        await using var verify = database.CreateContext();

        // Gone from every ordinary query, because of the global filter from feature 001...
        (await verify.Branches.AnyAsync(branch => branch.Id == id)).ShouldBeFalse();

        // ...but still a row, so its audit history survives (spec FR-011).
        var retained = await verify.Branches
            .IgnoreQueryFilters()
            .FirstOrDefaultAsync(branch => branch.Id == id);

        retained.ShouldNotBeNull();
        retained.IsDeleted.ShouldBeTrue();
        retained.DeletedAt.ShouldNotBeNull();

        // And the code it held is free again, which is what the filtered unique indexes are for: a
        // plain index would have retired the code along with the unit (spec FR-006).
        var recreated = await client.PostAsJsonAsync(
            OrganizationHarness.Route(OrganizationHarness.Branches),
            OrganizationHarness.NewUnit($"جديد {tag}", $"New {tag}", $"GONE{tag}"));

        recreated.EnsureSuccessStatusCode();
    }

    [Fact]
    public async Task The_database_refuses_a_duplicate_code_rather_than_trusting_a_prior_read()
    {
        // Two administrators creating the same code at the same moment must produce a refusal, and
        // a read-then-write check cannot guarantee that. This test writes twice without ever
        // checking, which is what a race amounts to.
        var code = $"RACE{OrganizationHarness.Tag()}";

        await using var context = database.CreateContext();

        context.Departments.Add(Department.Create("سباق", $"Race {code}", code));
        await context.SaveChangesAsync();

        await using var second = database.CreateContext();
        second.Departments.Add(Department.Create("سباق ثان", $"Race two {code}", code));

        await Should.ThrowAsync<DbUpdateException>(() => second.SaveChangesAsync());
    }

    [Fact]
    public async Task A_team_name_is_unique_within_its_department_only()
    {
        await using var context = database.CreateContext();

        var tag = OrganizationHarness.Tag();
        var first = Department.Create($"أ {tag}", $"Alpha {tag}", $"A{tag}");
        var second = Department.Create($"ب {tag}", $"Beta {tag}", $"B{tag}");

        context.Departments.AddRange(first, second);
        await context.SaveChangesAsync();

        context.Teams.Add(Team.Create(first, $"مستوى {tag}", $"Tier {tag}", $"AT{tag}"));
        await context.SaveChangesAsync();

        // The same name under a different department is permitted by the composite index...
        context.Teams.Add(Team.Create(second, $"مستوى {tag}", $"Tier {tag}", $"BT{tag}"));
        await Should.NotThrowAsync(() => context.SaveChangesAsync());

        // ...and refused under the same one.
        await using var third = database.CreateContext();
        var reloaded = await third.Departments.FirstAsync(department => department.Id == first.Id);
        third.Teams.Add(Team.Create(reloaded, $"مستوى {tag}", $"Tier {tag}", $"CT{tag}"));

        await Should.ThrowAsync<DbUpdateException>(() => third.SaveChangesAsync());
    }
}
