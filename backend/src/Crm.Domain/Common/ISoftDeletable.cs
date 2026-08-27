namespace Crm.Domain.Common;

/// <summary>
/// Marks an entity whose rows are retired rather than removed. Constitution VIII forbids hard
/// deletion of business records that require historical traceability.
/// Soft-deleted rows are excluded from queries by a global filter; suppressing that filter is
/// explicit and reviewable.
/// </summary>
public interface ISoftDeletable
{
    bool IsDeleted { get; set; }

    DateTimeOffset? DeletedAt { get; set; }

    Guid? DeletedBy { get; set; }
}
