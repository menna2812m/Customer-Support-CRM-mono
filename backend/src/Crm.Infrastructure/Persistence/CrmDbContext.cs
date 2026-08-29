using System.Linq.Expressions;
using System.Reflection;
using Crm.Domain.Common;
using Crm.Domain.Identity;
using Crm.Domain.Organization;
using Microsoft.EntityFrameworkCore;

namespace Crm.Infrastructure.Persistence;

/// <summary>
/// The single database context for the modular monolith.
///
/// The conventions below were established by feature 001 before any entity existed, so that the
/// first real ones - the identity tables added by feature 002 - inherited them without a decision
/// being made twice.
/// </summary>
public class CrmDbContext(DbContextOptions<CrmDbContext> options) : DbContext(options)
{
    public DbSet<User> Users => Set<User>();

    public DbSet<Role> Roles => Set<Role>();

    public DbSet<RolePermission> RolePermissions => Set<RolePermission>();

    public DbSet<RoleAssignment> RoleAssignments => Set<RoleAssignment>();

    public DbSet<Session> Sessions => Set<Session>();

    public DbSet<RenewalCredential> RenewalCredentials => Set<RenewalCredential>();

    public DbSet<AuthenticationEvent> AuthenticationEvents => Set<AuthenticationEvent>();

    public DbSet<Branch> Branches => Set<Branch>();

    public DbSet<Department> Departments => Set<Department>();

    public DbSet<Team> Teams => Set<Team>();

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        ArgumentNullException.ThrowIfNull(modelBuilder);

        base.OnModelCreating(modelBuilder);

        // One IEntityTypeConfiguration per entity, discovered rather than hand-registered.
        modelBuilder.ApplyConfigurationsFromAssembly(Assembly.GetExecutingAssembly());

        ApplySoftDeleteFilters(modelBuilder);
        RestrictDeletesByDefault(modelBuilder);
    }

    protected override void ConfigureConventions(ModelConfigurationBuilder configurationBuilder)
    {
        ArgumentNullException.ThrowIfNull(configurationBuilder);

        base.ConfigureConventions(configurationBuilder);

        // No unbounded nvarchar(max) by default - a column opts into a larger size explicitly.
        configurationBuilder.Properties<string>().HaveMaxLength(256);

        // Money and other decimals never inherit a provider default.
        configurationBuilder.Properties<decimal>().HavePrecision(18, 2);
    }

    /// <summary>
    /// Excludes retired rows from every query by default. Reading them back requires an explicit
    /// <c>IgnoreQueryFilters()</c>, which is visible in review.
    /// </summary>
    private static void ApplySoftDeleteFilters(ModelBuilder modelBuilder)
    {
        foreach (var entityType in modelBuilder.Model.GetEntityTypes())
        {
            if (!typeof(ISoftDeletable).IsAssignableFrom(entityType.ClrType))
            {
                continue;
            }

            var parameter = Expression.Parameter(entityType.ClrType, "e");
            var property = Expression.Property(parameter, nameof(ISoftDeletable.IsDeleted));
            entityType.SetQueryFilter(Expression.Lambda(Expression.Not(property), parameter));

            // The flag is read on every query, so it earns an index.
            var isDeleted = entityType.FindProperty(nameof(ISoftDeletable.IsDeleted));
            if (isDeleted is not null && entityType.FindIndex(isDeleted) is null)
            {
                entityType.AddIndex(isDeleted);
            }
        }
    }

    /// <summary>
    /// Cascading deletes are opt-in: an accidental cascade must not silently destroy business
    /// history (Constitution VIII). A relationship that genuinely needs a cascade re-enables it
    /// in its own configuration class, where a reviewer will see it.
    /// </summary>
    private static void RestrictDeletesByDefault(ModelBuilder modelBuilder)
    {
        foreach (var relationship in modelBuilder.Model
            .GetEntityTypes()
            .SelectMany(entityType => entityType.GetForeignKeys())
            .Where(fk => !fk.IsOwnership && fk.DeleteBehavior == DeleteBehavior.Cascade))
        {
            relationship.DeleteBehavior = DeleteBehavior.Restrict;
        }
    }
}
