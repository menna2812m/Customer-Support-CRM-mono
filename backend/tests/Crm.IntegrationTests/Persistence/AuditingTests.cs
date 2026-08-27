using Crm.Application.Abstractions;
using Crm.Domain.Common;
using Crm.Infrastructure.Persistence;
using Crm.Infrastructure.Persistence.Interceptors;
using Crm.IntegrationTests.Infrastructure;
using Microsoft.EntityFrameworkCore;
using Shouldly;

namespace Crm.IntegrationTests.Persistence;

/// <summary>
/// Spec FR-014, Constitution VIII: traceability stamps are applied by the persistence layer, and
/// a delete of a traceable record retires it rather than destroying it.
///
/// The probe entity lives only in this test context. The application schema stays empty, which is
/// what SC-010 requires.
/// </summary>
[Collection(DatabaseCollectionDefinition.Name)]
public sealed class AuditingTests(SqlServerFixture database) : IAsyncLifetime
{
    private static readonly Guid Actor = Guid.Parse("11111111-1111-1111-1111-111111111111");

    private readonly FakeClock _clock = new(DateTimeOffset.Parse("2026-08-26T10:00:00Z", null));
    private readonly string _probeDatabase = "Audit_" + Guid.CreateVersion7().ToString("n");

    public async ValueTask InitializeAsync()
    {
        await using var context = CreateContext();
        await context.Database.EnsureCreatedAsync();
    }

    public async ValueTask DisposeAsync()
    {
        await using var context = CreateContext();
        await context.Database.EnsureDeletedAsync();
    }

    [Fact]
    public async Task Insert_stamps_created_and_leaves_updated_empty()
    {
        await using var context = CreateContext();
        var probe = new AuditProbe { Name = "first" };

        context.Probes.Add(probe);
        await context.SaveChangesAsync();

        probe.CreatedAt.ShouldBe(_clock.GetUtcNow());
        probe.CreatedBy.ShouldBe(Actor);
        probe.UpdatedAt.ShouldBeNull();
        probe.UpdatedBy.ShouldBeNull();
    }

    [Fact]
    public async Task Update_stamps_updated_without_touching_created()
    {
        await using var context = CreateContext();
        var probe = new AuditProbe { Name = "before" };
        context.Probes.Add(probe);
        await context.SaveChangesAsync();

        var createdAt = probe.CreatedAt;
        _clock.Advance(TimeSpan.FromHours(1));

        probe.Name = "after";
        await context.SaveChangesAsync();

        probe.CreatedAt.ShouldBe(createdAt);
        probe.UpdatedAt.ShouldBe(_clock.GetUtcNow());
        probe.UpdatedBy.ShouldBe(Actor);
    }

    [Fact]
    public async Task Delete_retires_the_row_instead_of_removing_it()
    {
        await using var context = CreateContext();
        var probe = new AuditProbe { Name = "doomed" };
        context.Probes.Add(probe);
        await context.SaveChangesAsync();

        context.Probes.Remove(probe);
        await context.SaveChangesAsync();

        // Hidden from ordinary queries...
        (await context.Probes.CountAsync()).ShouldBe(0);

        // ...but still present, which is the whole point of soft deletion.
        var retired = await context.Probes.IgnoreQueryFilters().SingleAsync();
        retired.IsDeleted.ShouldBeTrue();
        retired.DeletedAt.ShouldBe(_clock.GetUtcNow());
        retired.DeletedBy.ShouldBe(Actor);
    }

    private ProbeDbContext CreateContext()
    {
        var connection = new Microsoft.Data.SqlClient.SqlConnectionStringBuilder(database.ConnectionString)
        {
            InitialCatalog = _probeDatabase,
        }.ConnectionString;

        var options = new DbContextOptionsBuilder<CrmDbContext>()
            .UseSqlServer(connection)
            .AddInterceptors(new AuditingSaveChangesInterceptor(new FakeCurrentUser(Actor), _clock))
            .Options;

        return new ProbeDbContext(options);
    }
}

/// <summary>Test-only entity: exercises the auditing and soft-delete conventions.</summary>
public sealed class AuditProbe : Entity, IAuditableEntity, ISoftDeletable
{
    public AuditProbe()
        : base(Guid.CreateVersion7()) { }

    public string Name { get; set; } = string.Empty;

    public DateTimeOffset CreatedAt { get; set; }

    public Guid? CreatedBy { get; set; }

    public DateTimeOffset? UpdatedAt { get; set; }

    public Guid? UpdatedBy { get; set; }

    public bool IsDeleted { get; set; }

    public DateTimeOffset? DeletedAt { get; set; }

    public Guid? DeletedBy { get; set; }
}

public sealed class ProbeDbContext(DbContextOptions<CrmDbContext> options) : CrmDbContext(options)
{
    public DbSet<AuditProbe> Probes => Set<AuditProbe>();
}

internal sealed class FakeCurrentUser(Guid? userId) : ICurrentUser
{
    public bool IsAuthenticated => userId is not null;

    public Guid? UserId => userId;

    public CallerPopulation? Population => CallerPopulation.Staff;

    public IReadOnlySet<string> Permissions { get; } = new HashSet<string>();

    public OrganizationScope? Scope => null;
}

internal sealed class FakeClock(DateTimeOffset start) : TimeProvider
{
    private DateTimeOffset _now = start;

    public override DateTimeOffset GetUtcNow() => _now;

    public void Advance(TimeSpan by) => _now = _now.Add(by);
}
