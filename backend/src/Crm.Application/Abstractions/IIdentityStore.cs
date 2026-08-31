namespace Crm.Application.Abstractions;

/// <summary>
/// The CRM's own record of who may sign in. Kept as an abstraction so the sign-in use case stays
/// free of persistence (Constitution I).
/// </summary>
public interface IIdentityStore
{
    /// <summary>
    /// Finds a user by the provider that issued their subject, together with the subject. This is
    /// the only lookup used to recognise a returning person: names and email addresses change,
    /// subjects do not (spec FR-004).
    /// </summary>
    /// <remarks>
    /// The provider is part of the lookup because a subject is only unique within the issuer that
    /// minted it (spec FR-015a). With one provider configured the distinction is invisible; with
    /// two, matching on the subject alone is how one person becomes somebody else.
    /// </remarks>
    Task<UserRecord?> FindBySubjectAsync(
        string provider,
        string providerSubject,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Every person holding the address, bound or not. Used **only** to decide a claim, never to
    /// authenticate somebody (spec FR-005) - an email match is not an identity match.
    /// </summary>
    /// <remarks>
    /// A list rather than one record, because the claim decision must be able to tell "exactly one
    /// prepared record" from "several" (spec FR-016, FR-017). Today the filtered unique index means
    /// it never returns more than one; the decision handles more anyway, and would rather be handed
    /// the truth than a first row.
    /// </remarks>
    Task<IReadOnlyList<UserRecord>> FindAllByEmailAsync(
        string email,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Binds a prepared person to the identity that just arrived, and records the sign-in
    /// (spec FR-016, FR-020).
    /// </summary>
    /// <remarks>
    /// Nothing prepared is disturbed: the roles and placement an administrator arranged are exactly
    /// what the person signs in with. Binding is refused outright on a person who already has an
    /// identity, in the domain rather than here (INV-5).
    /// </remarks>
    Task<UserRecord> ClaimAsync(
        Guid personId,
        string provider,
        string providerSubject,
        string email,
        string displayName,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Creates a user. Placement is deliberately not a parameter: a new user has none, and the CRM
    /// - not the provider - assigns it later (spec FR-018).
    /// </summary>
    Task<UserRecord> ProvisionAsync(
        string provider,
        string providerSubject,
        string email,
        string displayName,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Refreshes what the provider owns - name and email - and records the sign-in time. Placement
    /// is not refreshed, because the provider does not own it (spec FR-018).
    /// </summary>
    Task RefreshAsync(
        Guid userId,
        string email,
        string displayName,
        CancellationToken cancellationToken = default);

    /// <summary>The union of the permissions the user's roles grant (spec FR-021).</summary>
    Task<IReadOnlySet<string>> GetEffectivePermissionsAsync(Guid userId, CancellationToken cancellationToken = default);

    Task<bool> HasAnyRoleAsync(Guid userId, CancellationToken cancellationToken = default);

    /// <summary>
    /// Grants a role by name. Returns false when no such role is seeded, so a misconfigured default
    /// role is visible rather than silently ignored.
    /// </summary>
    Task<bool> GrantRoleAsync(Guid userId, string roleName, CancellationToken cancellationToken = default);

    Task<UserRecord?> FindByIdAsync(Guid userId, CancellationToken cancellationToken = default);

    /// <summary>
    /// Marks the user inactive. Returns false when they were already inactive, so a repeated
    /// deactivation does not produce a second audit record for something that did not happen.
    /// </summary>
    Task<bool> DeactivateAsync(Guid userId, CancellationToken cancellationToken = default);

    /// <summary>
    /// Every permission granted by a seeded role, with the role that grants it. Read at startup and
    /// checked against the catalog, so a permission that no longer exists is reported rather than
    /// quietly granting nothing (spec FR-024).
    /// </summary>
    Task<IReadOnlyList<RolePermissionGrant>> GetRolePermissionsAsync(CancellationToken cancellationToken = default);
}

/// <summary>One row of the role-to-permission store, as the startup check reads it.</summary>
public sealed record RolePermissionGrant(string RoleName, string Permission);

/// <summary>What the sign-in use case needs to know about a user. Not the entity.</summary>
public sealed record UserRecord(
    Guid Id,
    string? ProviderSubject,
    string Email,
    string DisplayName,
    bool IsActive,
    OrganizationScope? Scope)
{
    /// <summary>
    /// Whether a real identity has been bound yet. False means prepared and not yet arrived - the
    /// only kind of record a first sign-in may claim (spec FR-016).
    /// </summary>
    public bool HasBoundIdentity => ProviderSubject is not null;
}
