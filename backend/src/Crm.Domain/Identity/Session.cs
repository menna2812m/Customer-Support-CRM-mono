using Crm.Domain.Common;

namespace Crm.Domain.Identity;

/// <summary>
/// One established sign-in. Revoked, never deleted - a session is part of the history of who had
/// access when (spec FR-010, FR-014, Constitution VIII).
/// </summary>
public sealed class Session : Entity, IAuditableEntity
{
    private Session()
        : base(NewId()) { }

    public static Session Start(
        Guid userId,
        DateTimeOffset now,
        TimeSpan inactivityLimit,
        TimeSpan absoluteLimit,
        string? clientDescription,
        string? ipAddress) =>
        new()
        {
            UserId = userId,
            StartedAt = now,
            LastActivityAt = now,
            InactivityLimit = inactivityLimit,
            AbsoluteExpiresAt = now.Add(absoluteLimit),
            ClientDescription = clientDescription,
            IpAddressAtCreation = ipAddress,
        };

    public Guid UserId { get; private set; }

    public DateTimeOffset StartedAt { get; private set; }

    public DateTimeOffset LastActivityAt { get; private set; }

    public TimeSpan InactivityLimit { get; private set; }

    public DateTimeOffset AbsoluteExpiresAt { get; private set; }

    public DateTimeOffset? RevokedAt { get; private set; }

    public string? RevokedReason { get; private set; }

    /// <summary>Coarse client summary so a person recognises their own sessions. Not a fingerprint.</summary>
    public string? ClientDescription { get; private set; }

    public string? IpAddressAtCreation { get; private set; }

    public DateTimeOffset CreatedAt { get; set; }

    public Guid? CreatedBy { get; set; }

    public DateTimeOffset? UpdatedAt { get; set; }

    public Guid? UpdatedBy { get; set; }

    public bool IsRevoked => RevokedAt is not null;

    /// <summary>
    /// A session is usable while it is unrevoked, within its absolute lifetime, and has been used
    /// inside the inactivity window. There is no suspended state - it is usable or it is not.
    /// </summary>
    public bool IsActive(DateTimeOffset now) =>
        !IsRevoked && now < AbsoluteExpiresAt && now - LastActivityAt < InactivityLimit;

    public void RecordActivity(DateTimeOffset now) => LastActivityAt = now;

    public void Revoke(DateTimeOffset now, string reason)
    {
        if (IsRevoked)
        {
            return;
        }

        RevokedAt = now;
        RevokedReason = reason;
    }
}

/// <summary>Why a session ended. Recorded so an unexpected sign-out can be explained afterwards.</summary>
public static class SessionRevocationReason
{
    public const string SignedOut = "signed_out";
    public const string SignedOutEverywhere = "signed_out_everywhere";
    public const string CredentialReused = "credential_reused";
    public const string UserDeactivated = "user_deactivated";
}

/// <summary>
/// The single-use value that extends a session (spec FR-013). Only a hash is stored: a database
/// leak must not hand over live sessions.
/// </summary>
public sealed class RenewalCredential : Entity
{
    private RenewalCredential()
        : base(NewId()) { }

    public static RenewalCredential Issue(Guid sessionId, string tokenHash, DateTimeOffset expiresAt) =>
        new() { SessionId = sessionId, TokenHash = tokenHash, ExpiresAt = expiresAt };

    public Guid SessionId { get; private set; }

    public string TokenHash { get; private set; } = string.Empty;

    public DateTimeOffset ExpiresAt { get; private set; }

    public DateTimeOffset? UsedAt { get; private set; }

    /// <summary>The credential issued in its place - the rotation chain, useful when investigating.</summary>
    public Guid? ReplacedById { get; private set; }

    public bool IsSpent => UsedAt is not null;

    public void Spend(DateTimeOffset now, Guid replacedBy)
    {
        UsedAt = now;
        ReplacedById = replacedBy;
    }
}
