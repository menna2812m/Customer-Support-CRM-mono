using Crm.Domain.Common;

namespace Crm.Domain.Identity;

/// <summary>
/// The audit trail for authentication (spec FR-039). Persisted as well as logged, so a security
/// question can be answered without reading application log files.
///
/// <see cref="Detail"/> is human-readable context and never carries a credential, token, or hash.
/// </summary>
public sealed class AuthenticationEvent : Entity
{
    private AuthenticationEvent()
        : base(NewId()) { }

    public static AuthenticationEvent Record(
        string action,
        string outcome,
        DateTimeOffset occurredAt,
        string correlationId,
        Guid? userId = null,
        string? subjectReference = null,
        Guid? sessionId = null,
        string? ipAddress = null,
        string? detail = null) =>
        new()
        {
            Action = action,
            Outcome = outcome,
            OccurredAt = occurredAt,
            CorrelationId = correlationId,
            UserId = userId,
            SubjectReference = subjectReference,
            SessionId = sessionId,
            IpAddress = ipAddress,
            Detail = detail,
        };

    public DateTimeOffset OccurredAt { get; private set; }

    public string Action { get; private set; } = string.Empty;

    public string Outcome { get; private set; } = string.Empty;

    /// <summary>Null when no user could be resolved - a refused attempt still gets a record.</summary>
    public Guid? UserId { get; private set; }

    /// <summary>Subject or email the attempt referenced, so a refusal is investigable.</summary>
    public string? SubjectReference { get; private set; }

    public Guid? SessionId { get; private set; }

    public string? IpAddress { get; private set; }

    public string CorrelationId { get; private set; } = string.Empty;

    public string? Detail { get; private set; }
}

/// <summary>Stable action identifiers for the authentication audit trail.</summary>
public static class AuthenticationActions
{
    public const string SignInSucceeded = "sign_in.succeeded";
    public const string SignInRefused = "sign_in.refused";
    public const string SignInCollision = "sign_in.collision";
    public const string SessionRenewed = "session.renewed";
    public const string SessionRevoked = "session.revoked";
    public const string CredentialReused = "credential.reused";
    public const string RoleGranted = "role.granted";
}

/// <summary>Outcome values used by <see cref="AuthenticationEvent"/>.</summary>
public static class AuthenticationOutcomes
{
    public const string Succeeded = "succeeded";
    public const string Refused = "refused";
}
