using Crm.IntegrationTests.Infrastructure;
using Microsoft.EntityFrameworkCore;
using Shouldly;

namespace Crm.IntegrationTests.Persistence;

/// <summary>
/// Spec FR-011, FR-012, SC-010: the baseline migration applies to an empty database, is
/// re-runnable, and creates no business tables - this feature persists no business data.
/// </summary>
[Collection(DatabaseCollectionDefinition.Name)]
public sealed class BaselineMigrationTests(SqlServerFixture database)
{
    [Fact]
    public async Task Baseline_applies_and_leaves_no_pending_migrations()
    {
        await using var context = database.CreateContext();

        var applied = await context.Database.GetAppliedMigrationsAsync();
        var pending = await context.Database.GetPendingMigrationsAsync();

        applied.ShouldNotBeEmpty();
        pending.ShouldBeEmpty();
    }

    [Fact]
    public async Task Migrating_a_second_time_is_a_no_op()
    {
        await using var context = database.CreateContext();

        // Re-running must not throw: deployments retry, and two nodes may start together.
        await context.Database.MigrateAsync();

        (await context.Database.GetPendingMigrationsAsync()).ShouldBeEmpty();
    }

    [Fact]
    public async Task Schema_contains_no_business_tables()
    {
        await using var context = database.CreateContext();

        var tables = await context.Database
            .SqlQuery<string>(
                $"SELECT TABLE_NAME AS Value FROM INFORMATION_SCHEMA.TABLES WHERE TABLE_TYPE = 'BASE TABLE'")
            .ToListAsync();

        // Only EF's migration history may exist. A business table appearing here means this
        // feature has drifted outside its scope.
        tables.ShouldAllBe(name => name == "__EFMigrationsHistory");
    }
}
