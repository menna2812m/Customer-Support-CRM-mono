namespace Crm.Application.Organization;

/// <summary>
/// Why a change to the organization was refused. Each maps to a distinct, translatable error code,
/// so the client shows a specific message rather than a generic conflict.
/// </summary>
public enum OrganizationRefusal
{
    /// <summary>No such unit, or it has been deleted.</summary>
    NotFound,

    /// <summary>Another live unit of this kind already holds the code (spec FR-006).</summary>
    CodeConflict,

    /// <summary>
    /// Another live unit already holds one of the names - among units of this kind for a branch or
    /// department, or within the department for a team (spec FR-005).
    /// </summary>
    NameConflict,

    /// <summary>Teams belong to it, or people are placed in it (spec FR-012).</summary>
    HasDependents,

    /// <summary>A team cannot be moved into a department that is not active (spec FR-016).</summary>
    DepartmentInactive,
}

/// <summary>
/// The result of an operation that can be refused for a named reason. Kept free of HTTP types so
/// the Application layer stays independent of the web framework (Constitution I); the API layer
/// turns a refusal into the shared error contract.
/// </summary>
public sealed record OrganizationOutcome<TValue>
{
    internal OrganizationOutcome(TValue? value, OrganizationRefusal? refusal, string? detail)
    {
        Value = value;
        Refusal = refusal;
        Detail = detail;
    }

    public TValue? Value { get; }

    public OrganizationRefusal? Refusal { get; }

    /// <summary>Names what conflicted, so the refusal can be acted on rather than merely read.</summary>
    public string? Detail { get; }

    public bool Succeeded => Refusal is null;

}

/// <summary>
/// Creates outcomes. A non-generic companion, because static members on a generic type force every
/// caller to name the type argument twice and the analyzers rightly object (CA1000).
/// </summary>
public static class OrganizationOutcome
{
    public static OrganizationOutcome<TValue> Success<TValue>(TValue value) => new(value, null, null);

    public static OrganizationOutcome<TValue> Refused<TValue>(
        OrganizationRefusal refusal,
        string? detail = null) => new(default, refusal, detail);
}
