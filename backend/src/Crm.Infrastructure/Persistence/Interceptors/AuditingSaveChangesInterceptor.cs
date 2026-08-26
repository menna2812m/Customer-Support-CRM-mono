using Crm.Application.Abstractions;
using Crm.Domain.Common;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.ChangeTracking;
using Microsoft.EntityFrameworkCore.Diagnostics;

namespace Crm.Infrastructure.Persistence.Interceptors;

/// <summary>
/// Applies the traceability stamps required by Constitution VIII, and converts a delete of a
/// soft-deletable entity into a retirement. Because this runs inside SaveChanges, a handler
/// cannot forget to stamp a row - the rule is enforced rather than remembered.
/// </summary>
public sealed class AuditingSaveChangesInterceptor(ICurrentUser currentUser, TimeProvider clock)
    : SaveChangesInterceptor
{
    public override InterceptionResult<int> SavingChanges(
        DbContextEventData eventData,
        InterceptionResult<int> result)
    {
        Apply(eventData.Context);
        return base.SavingChanges(eventData, result);
    }

    public override ValueTask<InterceptionResult<int>> SavingChangesAsync(
        DbContextEventData eventData,
        InterceptionResult<int> result,
        CancellationToken cancellationToken = default)
    {
        Apply(eventData.Context);
        return base.SavingChangesAsync(eventData, result, cancellationToken);
    }

    private void Apply(DbContext? context)
    {
        if (context is null)
        {
            return;
        }

        var now = clock.GetUtcNow();
        var actor = currentUser.UserId;

        foreach (var entry in context.ChangeTracker.Entries<IAuditableEntity>())
        {
            switch (entry.State)
            {
                case EntityState.Added:
                    entry.Entity.CreatedAt = now;
                    entry.Entity.CreatedBy = actor;
                    entry.Entity.UpdatedAt = null;
                    entry.Entity.UpdatedBy = null;
                    break;

                case EntityState.Modified:
                    // Created values are immutable once written.
                    entry.Property(nameof(IAuditableEntity.CreatedAt)).IsModified = false;
                    entry.Property(nameof(IAuditableEntity.CreatedBy)).IsModified = false;
                    entry.Entity.UpdatedAt = now;
                    entry.Entity.UpdatedBy = actor;
                    break;

                default:
                    break;
            }
        }

        foreach (var entry in context.ChangeTracker.Entries<ISoftDeletable>())
        {
            if (entry.State != EntityState.Deleted)
            {
                continue;
            }

            RetireInsteadOfDeleting(entry, now, actor);
        }
    }

    private static void RetireInsteadOfDeleting(
        EntityEntry<ISoftDeletable> entry,
        DateTimeOffset now,
        Guid? actor)
    {
        entry.State = EntityState.Modified;
        entry.Entity.IsDeleted = true;
        entry.Entity.DeletedAt = now;
        entry.Entity.DeletedBy = actor;

        if (entry.Entity is IAuditableEntity auditable)
        {
            entry.Property(nameof(IAuditableEntity.CreatedAt)).IsModified = false;
            entry.Property(nameof(IAuditableEntity.CreatedBy)).IsModified = false;
            auditable.UpdatedAt = now;
            auditable.UpdatedBy = actor;
        }
    }
}
