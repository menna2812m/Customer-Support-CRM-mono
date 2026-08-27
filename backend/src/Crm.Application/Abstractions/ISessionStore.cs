namespace Crm.Application.Abstractions;

/// <summary>
/// Sessions live server-side so that revocation is immediate rather than eventual (spec FR-014).
/// A self-contained credential with no server state cannot be revoked before it expires, which
/// would make sign-out a lie for up to the credential's lifetime.
/// </summary>
public interface ISessionStore
{
    Task<SessionSnapshot> StartAsync(
        Guid userId,
        string? clientDescription,
        string? ipAddress,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Spends the presented renewal credential and issues a replacement. Presenting one that was
    /// already spent revokes the whole session and reports it as reuse (spec FR-013).
    /// </summary>
    Task<SessionRenewalResult> RenewAsync(string renewalCredential, CancellationToken cancellationToken = default);

    /// <summary>Whether the session is unrevoked and inside both of its limits.</summary>
    Task<bool> IsActiveAsync(Guid sessionId, CancellationToken cancellationToken = default);

    Task RevokeAsync(Guid sessionId, string reason, CancellationToken cancellationToken = default);

    Task<int> RevokeAllForUserAsync(Guid userId, string reason, CancellationToken cancellationToken = default);
}

/// <param name="SessionId">Identifies the session in credentials and in the audit trail.</param>
/// <param name="RenewalCredential">The value to place in the cookie. Only its hash is stored.</param>
/// <param name="RenewalExpiresAt">When the cookie should expire.</param>
public sealed record SessionSnapshot(Guid SessionId, string RenewalCredential, DateTimeOffset RenewalExpiresAt);

/// <summary>
/// The outcome of a renewal attempt. A failure is deliberately not an exception: expiry is an
/// ordinary event in the life of a session, not an error condition.
/// </summary>
public sealed record SessionRenewalResult(
    bool Succeeded,
    Guid? SessionId,
    Guid? UserId,
    SessionSnapshot? Renewed,
    string? FailureReason)
{
    public static SessionRenewalResult Failed(string reason) => new(false, null, null, null, reason);
}
