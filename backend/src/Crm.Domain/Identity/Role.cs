using System.Diagnostics.CodeAnalysis;
using Crm.Domain.Common;

namespace Crm.Domain.Identity;

/// <summary>
/// A named set of permissions, seeded by migration until the users-and-permissions feature adds a
/// screen (spec FR-020, FR-022).
/// </summary>
public sealed class Role : Entity, IAuditableEntity
{
    private Role()
        : base(NewId()) { }

    public static Role Create(string name, string description, bool isSystem = true) =>
        new() { Name = name, Description = description, IsSystem = isSystem };

    public string Name { get; private set; } = string.Empty;

    public string Description { get; private set; } = string.Empty;

    /// <summary>System roles are seeded and may not be deleted by a later administration screen.</summary>
    public bool IsSystem { get; private set; }

    public DateTimeOffset CreatedAt { get; set; }

    public Guid? CreatedBy { get; set; }

    public DateTimeOffset? UpdatedAt { get; set; }

    public Guid? UpdatedBy { get; set; }
}

/// <summary>A permission a role grants. The value must exist in the application's catalog.</summary>
[SuppressMessage(
    "Naming",
    "CA1711:Identifiers should not have incorrect suffix",
    Justification = "The reserved -Permission suffix refers to the legacy code-access-security type, "
        + "which is unrelated. RolePermission is the domain name for this join, and renaming it would "
        + "cost clarity to satisfy a rule about a type this codebase never uses.")]
public sealed class RolePermission
{
    public Guid RoleId { get; set; }

    public string Permission { get; set; } = string.Empty;
}

/// <summary>Which roles a user holds. Effective permissions are the union (spec FR-021).</summary>
public sealed class RoleAssignment
{
    public Guid UserId { get; set; }

    public Guid RoleId { get; set; }

    public DateTimeOffset GrantedAt { get; set; }

    /// <summary>Null when granted by deployment or by the default-role rule; the audit record says which.</summary>
    public Guid? GrantedBy { get; set; }
}
