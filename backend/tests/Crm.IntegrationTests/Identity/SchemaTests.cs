using Crm.Domain.Identity;
using Crm.Infrastructure.Persistence;
using Crm.IntegrationTests.Infrastructure;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Shouldly;

namespace Crm.IntegrationTests.Identity;

/// <summary>
/// The two index changes feature 004 makes, tested against a real SQL Server because that is the
/// only place they exist. Both replace a plain unique index with a filtered one, and in both cases
/// the filter is the whole point rather than an optimisation.
/// </summary>
[Collection(DatabaseCollectionDefinition.Name)]
public sealed class SchemaTests(SqlServerFixture database)
{
    private const string Provider = "https://tests.local/realms/crm";

    /// <summary>
    /// Spec FR-013. The case a plain unique index on a nullable column would reject: SQL Server
    /// treats NULL as a value and permits exactly one row to hold it, so without the filter the
    /// second person ever prepared would be refused by a constraint that looks correct.
    /// </summary>
    [Fact]
    public async Task Two_people_can_be_prepared_before_either_has_an_identity()
    {
        await using var harness = SignInHarness.Create(database.ConnectionString);
        using var scope = harness.Services.CreateScope();
        var context = scope.ServiceProvider.GetRequiredService<CrmDbContext>();

        var tag = Guid.NewGuid().ToString("n")[..8];

        context.Users.Add(User.PreProvision($"first-{tag}@tests.local", "First", 1));
        context.Users.Add(User.PreProvision($"second-{tag}@tests.local", "Second", 1));

        await Should.NotThrowAsync(() => context.SaveChangesAsync());

        var prepared = await context.Users
            .AsNoTracking()
            .CountAsync(user => user.ProviderSubject == null && user.Email.EndsWith($"-{tag}@tests.local"));

        prepared.ShouldBe(2);
    }

    /// <summary>
    /// Spec FR-015a. A subject is only unique within the provider that issued it, so the same
    /// subject string from two issuers is two people rather than a conflict.
    /// </summary>
    [Fact]
    public async Task The_same_subject_from_two_providers_is_two_people()
    {
        await using var harness = SignInHarness.Create(database.ConnectionString);
        using var scope = harness.Services.CreateScope();
        var context = scope.ServiceProvider.GetRequiredService<CrmDbContext>();

        var tag = Guid.NewGuid().ToString("n")[..8];
        var sharedSubject = $"subject-{tag}";

        context.Users.Add(User.Provision(
            $"{Provider}/one", sharedSubject, $"one-{tag}@tests.local", "One", 1, OrganizationPlacement.None));
        context.Users.Add(User.Provision(
            $"{Provider}/two", sharedSubject, $"two-{tag}@tests.local", "Two", 1, OrganizationPlacement.None));

        await Should.NotThrowAsync(() => context.SaveChangesAsync());
    }

    /// <summary>The same identity twice is still a conflict, and the database is what refuses it.</summary>
    [Fact]
    public async Task The_same_identity_twice_is_refused_by_the_database()
    {
        await using var harness = SignInHarness.Create(database.ConnectionString);
        using var scope = harness.Services.CreateScope();
        var context = scope.ServiceProvider.GetRequiredService<CrmDbContext>();

        var tag = Guid.NewGuid().ToString("n")[..8];
        var subject = $"subject-{tag}";

        context.Users.Add(User.Provision(
            Provider, subject, $"a-{tag}@tests.local", "A", 1, OrganizationPlacement.None));
        await context.SaveChangesAsync();

        context.Users.Add(User.Provision(
            Provider, subject, $"b-{tag}@tests.local", "B", 1, OrganizationPlacement.None));

        await Should.ThrowAsync<DbUpdateException>(() => context.SaveChangesAsync());
    }

    /// <summary>
    /// Spec FR-026 and FR-014. A deleted person releases their address, so a record created by
    /// typo can be fixed by deleting it and adding it again - while a live duplicate is still
    /// refused by the database rather than only by a prior read.
    /// </summary>
    [Fact]
    public async Task A_deleted_person_releases_their_address_while_a_live_duplicate_is_refused()
    {
        await using var harness = SignInHarness.Create(database.ConnectionString);
        using var scope = harness.Services.CreateScope();
        var context = scope.ServiceProvider.GetRequiredService<CrmDbContext>();

        var email = $"reused-{Guid.NewGuid():n}@tests.local";

        var first = User.PreProvision(email, "First", 1);
        context.Users.Add(first);
        await context.SaveChangesAsync();

        // A second live person on the same address is refused by the index, not by a read-then-write.
        context.Users.Add(User.PreProvision(email, "Duplicate", 1));
        await Should.ThrowAsync<DbUpdateException>(() => context.SaveChangesAsync());

        context.ChangeTracker.Clear();

        // Soft-delete the original, and the address becomes available again.
        var stored = await context.Users.SingleAsync(user => user.Email == email);
        stored.IsDeleted = true;
        stored.DeletedAt = DateTimeOffset.UtcNow;
        await context.SaveChangesAsync();

        context.Users.Add(User.PreProvision(email, "Replacement", 1));

        await Should.NotThrowAsync(() => context.SaveChangesAsync());
    }
}
