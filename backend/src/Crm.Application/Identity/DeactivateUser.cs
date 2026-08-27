using Crm.Application.Abstractions;
using Crm.Domain.Identity;
using Microsoft.Extensions.Logging;

namespace Crm.Application.Identity;

/// <summary>
/// Removing somebody's access (spec FR-015).
///
/// Deactivating a user without ending their sessions would leave them working normally until their
/// last credential expired - up to the access-credential lifetime after a decision that was meant
/// to take effect at once. Somebody who has just been dismissed is exactly the person for whom
/// "eventually" is the wrong answer, so the two steps are one operation rather than two calls a
/// future caller might make separately.
///
/// The user-management feature will own the endpoint; the rule lives here so that whatever calls
/// it cannot get the ordering wrong.
/// </summary>
public sealed class DeactivateUser(
    IIdentityStore identityStore,
    ISessionStore sessions,
    IAuthenticationEventLog events,
    ILogger<DeactivateUser> logger)
{
    /// <summary>
    /// Deactivates the user and revokes every live session they hold. Returns how many sessions
    /// were ended, or null when there was no active user to deactivate.
    /// </summary>
    public async Task<int?> ExecuteAsync(Guid userId, CancellationToken cancellationToken = default)
    {
        if (!await identityStore.DeactivateAsync(userId, cancellationToken))
        {
            return null;
        }

        var revoked = await sessions.RevokeAllForUserAsync(
            userId,
            SessionRevocationReason.UserDeactivated,
            cancellationToken);

        await events.RecordUserDeactivatedAsync(userId, revoked, cancellationToken);

        logger.LogWarning(
            "User {UserId} was deactivated and {SessionCount} live session(s) were revoked.",
            userId,
            revoked);

        return revoked;
    }
}
