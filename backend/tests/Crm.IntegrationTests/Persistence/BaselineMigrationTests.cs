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
    public async Task The_schema_contains_only_the_tables_the_delivered_features_declare()
    {
        await using var context = database.CreateContext();

        var tables = await context.Database
            .SqlQuery<string>(
                $"SELECT TABLE_NAME AS Value FROM INFORMATION_SCHEMA.TABLES WHERE TABLE_TYPE = 'BASE TABLE'")
            .ToListAsync();

        // Feature 001 asserted that nothing but the migration history existed - the scope guard for
        // a foundation that deliberately persisted nothing. Feature 002 adds the identity tables, so
        // the guard now names them. It still catches drift: a customer or ticket table appearing
        // here means a feature has strayed outside its specification.
        // Ordinal order: the underscore-prefixed migration history sorts after the letters.
        string[] expected =
        [
            "AuthenticationEvent",
            "RenewalCredential",
            "Role",
            "RoleAssignment",
            "RolePermission",
            "Session",
            "User",
            "__EFMigrationsHistory",
        ];

        tables.Order(StringComparer.Ordinal).ShouldBe(expected);
    }
}
