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
        string provider,
        string providerSubject,
        string email,
        string displayName,
        int population,
        OrganizationPlacement placement)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(provider);
        ArgumentException.ThrowIfNullOrWhiteSpace(providerSubject);
        ArgumentException.ThrowIfNullOrWhiteSpace(email);

        return new User
        {
            Provider = provider,
            ProviderSubject = providerSubject,
            Email = NormalizeEmail(email),
            DisplayName = string.IsNullOrWhiteSpace(displayName) ? email : displayName,
            Population = population,
            DepartmentId = placement.DepartmentId,
            BranchId = placement.BranchId,
            TeamId = placement.TeamId,
        };
    }

    /// <summary>
    /// Creates somebody who has never signed in, so an administrator can arrange their roles and
    /// placement before their first day (spec FR-013).
    /// </summary>
    /// <remarks>
    /// The identity is absent rather than blank. "Prepared but not yet arrived" is expressed by
    /// having no subject at all, not by a status column beside one - two facts about the same thing
    /// can disagree, and this way there is only one fact.
    /// </remarks>
    public static User PreProvision(string email, string displayName, int population)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(email);

        var normalized = NormalizeEmail(email);

        return new User
        {
            Provider = null,
            ProviderSubject = null,
            Email = normalized,
            DisplayName = string.IsNullOrWhiteSpace(displayName) ? normalized : displayName,
            Population = population,
        };
    }

    /// <summary>Which identity provider issued <see cref="ProviderSubject"/>. Null until bound.</summary>
    /// <remarks>
    /// Recorded alongside the subject because a subject is only unique within the provider that
    /// issued it (spec FR-015a). With one provider configured the distinction is invisible; with two
    /// it is the difference between two people and one.
    /// </remarks>
    public string? Provider { get; private set; }

    /// <summary>
    /// Stable subject from the identity provider. Null until the first sign-in binds it.
    /// </summary>
    public string? ProviderSubject { get; private set; }

    /// <summary>Whether a real identity has been bound yet. False means prepared and not yet arrived.</summary>
    public bool HasBoundIdentity => ProviderSubject is not null;

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

    /// <summary>
    /// Binds this person to the identity that just signed in, permanently (spec FR-020, INV-5).
    /// </summary>
    /// <remarks>
    /// Refuses a person who already has one. An email address is a one-time bootstrap for finding a
    /// prepared record; it is never grounds for moving an established account to a different
    /// identity. The sign-in path refuses that case first and records the collision - this refuses
    /// it again, because the consequence of getting it wrong is somebody else's account.
    /// </remarks>
    public void BindIdentity(string provider, string providerSubject)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(provider);
        ArgumentException.ThrowIfNullOrWhiteSpace(providerSubject);

        if (HasBoundIdentity)
        {
            throw new InvalidOperationException(
                "This person is already bound to an identity. Rebinding is never permitted.");
        }

        Provider = provider;
        ProviderSubject = providerSubject;
    }

    /// <summary>
    /// Records where this person sits: a branch, and either a department or a team within one
    /// (spec FR-009, FR-010, FR-011).
    /// </summary>
    /// <remarks>
    /// When a team is given, the department comes from that team and is not accepted separately.
    /// Passing a department that disagrees is refused rather than quietly replaced: by the time a
    /// call reaches here the application layer has already refused a mismatch with
    /// <c>identity_placement_mismatch</c>, so a disagreement at this depth is a programming error,
    /// and silently storing something the caller did not ask for would hide it.
    ///
    /// Passing nothing clears the placement, which is how somebody is removed from a unit.
    /// </remarks>
    public void Place(Guid? branchId, Guid? departmentId, TeamPlacement? team)
    {
        BranchId = branchId;

        if (team is not { } assignment)
        {
            TeamId = null;
            DepartmentId = departmentId;
            return;
        }

        if (departmentId is { } named && named != assignment.DepartmentId)
        {
            throw new InvalidOperationException(
                "A placement named a department that disagrees with the team's department.");
        }

        TeamId = assignment.TeamId;
        DepartmentId = assignment.DepartmentId;
    }

    public void RecordSignIn(DateTimeOffset at) => LastSignedInAt = at;

    public void Deactivate() => IsActive = false;

    public void Reactivate() => IsActive = true;

    public static string NormalizeEmail(string email) => email.Trim().ToLowerInvariant();
}

/// <summary>
/// A team together with the department it belongs to.
/// </summary>
/// <remarks>
/// The pair travels as one value because the domain cannot reach a team to look its department up,
/// and because separating them is precisely how INV-2 gets broken - by setting one and forgetting
/// the other.
/// </remarks>
public readonly record struct TeamPlacement(Guid TeamId, Guid DepartmentId);

/// <summary>Organizational placement as asserted or stored. Absent means "sees nothing extra".</summary>
public readonly record struct OrganizationPlacement(Guid? DepartmentId, Guid? BranchId, Guid? TeamId)
{
    public static OrganizationPlacement None => new(null, null, null);

    public bool HasAny => DepartmentId is not null || BranchId is not null || TeamId is not null;
}
