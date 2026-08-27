namespace Crm.Application.Abstractions;

/// <summary>
/// Records what happened at sign-in (spec FR-039).
///
/// Persisted as well as logged, so a security question can be answered without reading application
/// log files - and so a refusal, which by definition has no session to trace, still leaves a mark.
/// Nothing recorded here may contain a credential, token, or hash.
/// </summary>
public interface IAuthenticationEventLog
{
    Task RecordSuccessAsync(Guid userId, string providerSubject, CancellationToken cancellationToken = default);

    /// <param name="reason">A stable identifier such as <c>no_access</c> or <c>inactive</c>.</param>
    Task RecordRefusalAsync(
        string reason,
        string providerSubject,
        Guid? userId,
        CancellationToken cancellationToken = default);

    /// <summary>An unknown subject arrived with an email already held by <paramref name="existingUserId"/>.</summary>
    Task RecordCollisionAsync(
        string providerSubject,
        Guid existingUserId,
        CancellationToken cancellationToken = default);

    Task RecordRoleGrantAsync(
        Guid userId,
        string roleName,
        string grantedBecause,
        CancellationToken cancellationToken = default);

    Task RecordSessionRevokedAsync(
        Guid userId,
        Guid sessionId,
        string reason,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// A session was extended. Recorded because the renewal trail is what shows how long an
    /// account was actually in use, and because a gap in it is how a revocation is confirmed.
    /// </summary>
    Task RecordSessionRenewedAsync(Guid userId, Guid sessionId, CancellationToken cancellationToken = default);

    /// <param name="revokedSessions">How many live sessions the deactivation ended.</param>
    Task RecordUserDeactivatedAsync(
        Guid userId,
        int revokedSessions,
        CancellationToken cancellationToken = default);
}
