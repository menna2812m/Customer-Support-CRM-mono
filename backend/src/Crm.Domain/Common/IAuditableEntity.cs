namespace Crm.Domain.Common;

/// <summary>
/// Traceability stamps required by Constitution VIII. Values are applied automatically by the
/// persistence layer - handlers never assign them, so they cannot be forgotten.
/// </summary>
public interface IAuditableEntity
{
    /// <summary>Set once on insert. Never modified afterwards.</summary>
    DateTimeOffset CreatedAt { get; set; }

    /// <summary>Acting user at insert. Null only for system-initiated writes.</summary>
    Guid? CreatedBy { get; set; }

    /// <summary>Set on every update. Null while the row has never been updated.</summary>
    DateTimeOffset? UpdatedAt { get; set; }

    /// <summary>Acting user at the most recent update.</summary>
    Guid? UpdatedBy { get; set; }
}
