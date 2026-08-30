namespace Crm.Application.Common;

/// <summary>
/// Stable, machine-readable error identifiers carried in the <c>code</c> member of every failure
/// response. Clients switch on these and map them to translated messages - the human-readable
/// text in the response is for developers and logs only (spec LR-003).
/// Changing a value here is a breaking API change.
/// </summary>
public static class ErrorCodes
{
    public const string ValidationFailed = "validation_failed";
    public const string MalformedRequest = "malformed_request";
    public const string UnsupportedApiVersion = "unsupported_api_version";
    public const string Unauthenticated = "unauthenticated";
    public const string Forbidden = "forbidden";
    public const string OriginNotAllowed = "origin_not_allowed";
    public const string NotFound = "not_found";
    public const string Conflict = "conflict";
    public const string UnexpectedError = "unexpected_error";

    /// <summary>Added by feature 002 (authentication). See contracts/session-contract.md.</summary>
    public const string SignInFailed = "sign_in_failed";

    public const string ProviderUnavailable = "provider_unavailable";

    public const string NoAccess = "no_access";

    public const string IdentityCollision = "identity_collision";

    public const string SessionExpired = "session_expired";

    public const string RateLimited = "rate_limited";

    /// <summary>Added by feature 003 (organization). See contracts/organization-api.yaml.</summary>
    public const string OrganizationCodeConflict = "organization_code_conflict";

    /// <summary>A department or branch name, or a team name within its department, is taken.</summary>
    public const string OrganizationNameConflict = "organization_name_conflict";

    /// <summary>
    /// A unit cannot be deleted while teams belong to it or people are placed in it. The detail
    /// names what depends on it, because a refusal that does not say why cannot be acted on.
    /// </summary>
    public const string OrganizationHasDependents = "organization_has_dependents";

    /// <summary>A team cannot be moved into a department that is not active.</summary>
    public const string OrganizationDepartmentInactive = "organization_department_inactive";

    /// <summary>Added by feature 004 (identity administration). See contracts/identity-api.yaml.</summary>
    public const string IdentityEmailInUse = "identity_email_in_use";

    /// <summary>
    /// A change was refused because it would leave the system with no active administrator. The
    /// only rule in the feature enforced by a transaction rather than by the schema, because no
    /// constraint can express "at least one row must remain".
    /// </summary>
    public const string IdentityLastAdministrator = "identity_last_administrator";

    /// <summary>
    /// An administrator may not remove their own administrator role, deactivate themselves, or
    /// delete themselves. Someone else must do it.
    /// </summary>
    public const string IdentitySelfDemotion = "identity_self_demotion";

    /// <summary>
    /// A placement named a department that disagrees with the given team's department. Refused
    /// rather than silently corrected: a caller sending a stale department has a bug, and storing
    /// something it did not ask for hides it.
    /// </summary>
    public const string IdentityPlacementMismatch = "identity_placement_mismatch";

    /// <summary>
    /// A sign-in presented an email address belonging to somebody already bound to a different
    /// provider subject. Refused and recorded for a person to resolve, because re-binding an
    /// established account from an email would be an account takeover with extra steps.
    /// </summary>
    public const string IdentitySubjectCollision = "identity_subject_collision";

    /// <summary>
    /// A pre-provisioned record was not claimed because the provider did not assert the address is
    /// verified. The claim fails closed and an ordinary account is created instead.
    /// </summary>
    public const string IdentityEmailNotVerified = "identity_email_not_verified";

    /// <summary>More than one unclaimed record matched the address, so none was claimed.</summary>
    public const string IdentityEmailAmbiguous = "identity_email_ambiguous";

    /// <summary>Per-field rule identifiers used inside the <c>errors</c> array.</summary>
    public static class Field
    {
        public const string Required = "required";
        public const string Range = "range";
        public const string MaxLength = "max_length";
        public const string Format = "format";
        public const string NotUnique = "not_unique";
        public const string NotSortable = "not_sortable";
        public const string UnknownParameter = "unknown_parameter";
        public const string TooManyItems = "too_many_items";
    }
}

/// <summary>
/// Documentation anchors for the <c>type</c> member. These identify an error class; they are not
/// live endpoints.
/// </summary>
public static class ProblemTypes
{
    private const string Base = "https://crm.azm.sa/errors/";

    public static string ForCode(string code) => Base + code.Replace('_', '-');
}
