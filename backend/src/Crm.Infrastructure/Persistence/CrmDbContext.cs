using System.Linq.Expressions;
using System.Reflection;
using Crm.Domain.Common;
using Microsoft.EntityFrameworkCore;

namespace Crm.Infrastructure.Persistence;

/// <summary>
/// The single database context for the modular monolith.
/// This feature introduces no business entity: the conventions below exist so the first real
/// entity inherits them without a decision being made twice
/// (specs/001-project-foundation/data-model.md).
/// </summary>
public class CrmDbContext(DbContextOptions<CrmDbContext> options) : DbContext(options)
{
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
