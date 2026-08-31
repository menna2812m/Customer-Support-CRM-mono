using Crm.Application.Abstractions;
using Crm.Application.Common;

namespace Crm.Application.Identity.Claiming;

/// <summary>
/// The audit trail for what a first sign-in did with the records it found (spec AR-008).
/// </summary>
/// <remarks>
/// Separate from the sign-in path so that what is recorded, and what is deliberately not, sits in
/// one readable place. Every entry carries the address and nothing else: no token, no claim dump,
/// no provider payload (Constitution XI). The address is the only thing that identifies the attempt
/// - a claim that was refused has, by definition, no person to name it by.
///
/// There is no actor. A sign-in is not somebody administering somebody else; it is an anonymous
/// attempt until it succeeds, which is what <see cref="AuditEntry.ActorId"/> being null means.
/// </remarks>
public sealed class ClaimAudit(
    IAuditRecorder audit,
    ICorrelationAccessor correlation,
    TimeProvider clock)
{
    /// <summary>
    /// A subject bound before the CRM tracked its issuer had that issuer recorded (FR-015a).
    /// </summary>
    /// <remarks>
    /// Recorded because it is a change to an identity, even though nobody asked for it and nothing
    /// about the person's access moves. A trail that showed the column full without ever showing it
    /// being filled would leave the question open.
    /// </remarks>
    public Task RecordProviderAdoptedAsync(
        Guid personId,
        string email,
        CancellationToken cancellationToken = default) =>
        RecordAsync("identity.person.provider_recorded", personId, email, reason: null, cancellationToken);

    /// <summary>A prepared record was bound to the identity that arrived for it (FR-020).</summary>
    public Task RecordClaimedAsync(Guid personId, string email, CancellationToken cancellationToken = default) =>
        RecordAsync("identity.person.claimed", personId, email, reason: null, cancellationToken);

    /// <summary>
    /// A prepared record matched and was not claimed (FR-017). Recorded because the administrator
    /// who prepared the address needs to be able to find out why nothing happened.
    /// </summary>
    /// <param name="reason">
    /// <c>identity_email_not_verified</c> or <c>identity_email_ambiguous</c> - the same code the
    /// person was shown, so the trail and the message agree.
    /// </param>
    public Task RecordRefusedClaimAsync(
        Guid? personId,
        string email,
        string reason,
        CancellationToken cancellationToken = default) =>
        RecordAsync("identity.claim.refused", personId, email, reason, cancellationToken);

    /// <summary>
    /// An address belonging to an established account was presented by a different subject (FR-018).
    /// Recorded for manual resolution: only a person can tell a reissued address from a duplicate.
    /// </summary>
    public Task RecordCollisionAsync(
        Guid personId,
        string email,
        CancellationToken cancellationToken = default) =>
        RecordAsync(
            "identity.claim.collision",
            personId,
            email,
            ErrorCodes.IdentitySubjectCollision,
            cancellationToken);

    private Task RecordAsync(
        string action,
        Guid? personId,
        string email,
        string? reason,
        CancellationToken cancellationToken)
    {
        var metadata = new Dictionary<string, string>(StringComparer.Ordinal) { ["email"] = email };

        if (reason is not null)
        {
            metadata["reason"] = reason;
        }

        return audit.RecordAsync(
            new AuditEntry(
                action,
                ActorId: null,
                personId is null ? null : "User",
                personId?.ToString(),
                clock.GetUtcNow(),
                correlation.CorrelationId,
                metadata),
            cancellationToken);
    }
}
