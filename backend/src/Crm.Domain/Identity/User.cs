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
    /// Refreshes what the provider owns: the email address and the display name, and nothing else.
    /// </summary>
    /// <remarks>
    /// Placement is conspicuously absent. Feature 002 let a provider-asserted value overwrite it;
    /// feature 003 made placement a foreign key to real records and gave the CRM sole ownership of
    /// it (spec FR-018), so sign-in no longer touches it at all. An administrator sets placement,
    /// and it survives every subsequent sign-in.
    /// </remarks>
    public void RefreshFromProvider(string email, string displayName)
    {
        Email = NormalizeEmail(email);
        DisplayName = string.IsNullOrWhiteSpace(displayName) ? Email : displayName;
    }

    /// <summary>
    /// Records that this person's department changed because their team moved (spec FR-015).
    /// </summary>
    /// <remarks>
    /// This exists so the invariant has one owner. A user's department must agree with their team's
    /// department whenever both are present (INV-2), and a team move is the only thing in this
    /// feature that can break that agreement. Feature 004 will place people directly and must
    /// maintain the same invariant.
    /// </remarks>
    public void PlaceInDepartment(Guid departmentId) => DepartmentId = departmentId;

    /// <summary>
    /// Places this person on a team, in the department that team belongs to.
    /// </summary>
    /// <remarks>
    /// The department is a parameter rather than something looked up, because the domain cannot
    /// reach the team. Passing both together is what makes INV-2 - a user's department agrees with
    /// their team's - impossible to break by setting one and forgetting the other. Feature 004 will
    /// use this from its placement screen.
    /// </remarks>
    public void PlaceOnTeam(Guid teamId, Guid departmentId)
    {
        TeamId = teamId;
        DepartmentId = departmentId;
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
