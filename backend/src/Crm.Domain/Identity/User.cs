using Crm.Domain.Common;

namespace Crm.Domain.Identity;

/// <summary>
/// A person who can sign in. Created on first successful sign-in and keyed on the identity
/// provider's stable subject - names and email addresses change, subjects do not (spec FR-004).
/// </summary>
public sealed class User : Entity, IAuditableEntity, ISoftDeletable, IHasOrganizationScope
{
    private User()
        : base(NewId()) { }

    public static User Provision(
        string providerSubject,
        string email,
        string displayName,
        int population,
        OrganizationPlacement placement)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(providerSubject);
        ArgumentException.ThrowIfNullOrWhiteSpace(email);

        return new User
        {
            ProviderSubject = providerSubject,
            Email = NormalizeEmail(email),
            DisplayName = string.IsNullOrWhiteSpace(displayName) ? email : displayName,
            Population = population,
            DepartmentId = placement.DepartmentId,
            BranchId = placement.BranchId,
            TeamId = placement.TeamId,
        };
    }

    /// <summary>Stable subject from the identity provider. The only key used to recognise a returning user.</summary>
    public string ProviderSubject { get; private set; } = string.Empty;

    /// <summary>Normalized lower-case. Unique; a conflict is refused and escalated (spec FR-005).</summary>
    public string Email { get; private set; } = string.Empty;

    public string DisplayName { get; private set; } = string.Empty;

    public int Population { get; private set; }

    public bool IsActive { get; private set; } = true;

    public Guid? DepartmentId { get; private set; }

    public Guid? BranchId { get; private set; }

    public Guid? TeamId { get; private set; }

    public DateTimeOffset? LastSignedInAt { get; private set; }

    public DateTimeOffset CreatedAt { get; set; }

    public Guid? CreatedBy { get; set; }

    public DateTimeOffset? UpdatedAt { get; set; }

    public Guid? UpdatedBy { get; set; }

    public bool IsDeleted { get; set; }

    public DateTimeOffset? DeletedAt { get; set; }

    public Guid? DeletedBy { get; set; }

    /// <summary>
    /// Refreshes what the provider owns. Placement is only overwritten when the provider actually
    /// asserted it, so a provider that carries no organizational data does not erase a value the
    /// organization feature will later populate (spec FR-026).
    /// </summary>
    public void RefreshFromProvider(string email, string displayName, OrganizationPlacement placement)
    {
        Email = NormalizeEmail(email);
        DisplayName = string.IsNullOrWhiteSpace(displayName) ? Email : displayName;

        if (placement.HasAny)
        {
            DepartmentId = placement.DepartmentId;
            BranchId = placement.BranchId;
            TeamId = placement.TeamId;
        }
    }

    public void RecordSignIn(DateTimeOffset at) => LastSignedInAt = at;

    public void Deactivate() => IsActive = false;

    public void Reactivate() => IsActive = true;

    public static string NormalizeEmail(string email) => email.Trim().ToLowerInvariant();
}

/// <summary>Organizational placement as asserted or stored. Absent means "sees nothing extra".</summary>
public readonly record struct OrganizationPlacement(Guid? DepartmentId, Guid? BranchId, Guid? TeamId)
{
    public static OrganizationPlacement None => new(null, null, null);

    public bool HasAny => DepartmentId is not null || BranchId is not null || TeamId is not null;
}
