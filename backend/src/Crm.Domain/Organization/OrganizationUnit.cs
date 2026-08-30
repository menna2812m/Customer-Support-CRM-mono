using Crm.Domain.Common;

namespace Crm.Domain.Organization;

/// <summary>
/// What every organizational unit has in common: two names, a stable code, and whether it is
/// active. Branches, departments, and teams differ only in their relationships, which is why the
/// shape lives here rather than three times over (spec FR-005, FR-006, FR-009).
/// </summary>
/// <remarks>
/// This base type is deliberately not an entity in the EF model. No <c>DbSet</c> exposes it and no
/// configuration names it, so each derived type maps to its own table rather than to a shared
/// hierarchy. That is a property of the configuration, not a guarantee - the generated migration is
/// reviewed to confirm three tables rather than one.
/// </remarks>
public abstract class OrganizationUnit : Entity, IAuditableEntity, ISoftDeletable
{
    protected OrganizationUnit()
        : base(NewId()) { }

    /// <summary>Arabic name. Required - a half-translated organization is the seam Constitution VII prevents.</summary>
    public string NameAr { get; private set; } = string.Empty;

    /// <summary>English name. Required for the same reason.</summary>
    public string NameEn { get; private set; } = string.Empty;

    /// <summary>
    /// Stable reference chosen by an administrator. Set once and never changed (spec FR-006), which
    /// is enforced by there being no way to change it rather than by a rule somebody must remember.
    /// A unit created with the wrong code is deleted and recreated.
    /// </summary>
    public string Code { get; private set; } = string.Empty;

    /// <summary>
    /// An inactive unit keeps its history and everyone already placed in it, and simply stops being
    /// offered for new placements (spec FR-009, FR-010).
    /// </summary>
    public bool IsActive { get; private set; } = true;

    public DateTimeOffset CreatedAt { get; set; }

    public Guid? CreatedBy { get; set; }

    public DateTimeOffset? UpdatedAt { get; set; }

    public Guid? UpdatedBy { get; set; }

    public bool IsDeleted { get; set; }

    public DateTimeOffset? DeletedAt { get; set; }

    public Guid? DeletedBy { get; set; }

    /// <summary>Changes the names. The code is not renameable and is absent by design.</summary>
    public void Rename(string nameAr, string nameEn)
    {
        NameAr = RequireText(nameAr, nameof(nameAr));
        NameEn = RequireText(nameEn, nameof(nameEn));
    }

    public void Activate() => IsActive = true;

    public void Deactivate() => IsActive = false;

    /// <summary>
    /// Normalizes a name or code for storage and comparison: trimmed, so that two values differing
    /// only by surrounding whitespace are the same value. Case is handled by the database's
    /// case-insensitive collation rather than by lowering here, which would make the uniqueness
    /// indexes unusable as indexes.
    /// </summary>
    public static string Normalize(string? value) => value?.Trim() ?? string.Empty;

    /// <summary>Sets the values a unit is created with. Called once, by a derived factory.</summary>
    protected void Initialize(string nameAr, string nameEn, string code)
    {
        Rename(nameAr, nameEn);
        Code = RequireText(code, nameof(code));
    }

    private static string RequireText(string? value, string parameterName)
    {
        var normalized = Normalize(value);

        return normalized.Length == 0
            ? throw new ArgumentException("An organizational unit requires this value.", parameterName)
            : normalized;
    }
}
