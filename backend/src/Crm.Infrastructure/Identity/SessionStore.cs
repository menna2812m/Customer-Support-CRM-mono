using System.Security.Cryptography;
using Crm.Application.Abstractions;
using Crm.Domain.Identity;
using Crm.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;

namespace Crm.Infrastructure.Identity;

/// <summary>
/// Server-side sessions with rotating, single-use renewal credentials (spec FR-011 to FR-014).
///
/// Two properties matter more than the code that produces them. Revocation is immediate, because
/// the session is a row rather than a claim in a self-contained token. And a renewal credential
/// that is presented twice revokes the whole session rather than being treated as a race: the
/// legitimate client holds exactly one and replaces it atomically, so a second presentation means
/// somebody else has a copy.
/// </summary>
public sealed class SessionStore(
    CrmDbContext context,
    IOptions<SessionSettings> settings,
    TimeProvider clock) : ISessionStore
{
    public async Task<SessionSnapshot> StartAsync(
        Guid userId,
        string? clientDescription,
        string? ipAddress,
        CancellationToken cancellationToken = default)
    {
        var options = settings.Value;
        var now = clock.GetUtcNow();

        var session = Session.Start(
            userId,
            now,
            TimeSpan.FromHours(options.InactivityHours),
            TimeSpan.FromHours(options.AbsoluteHours),
            clientDescription,
            ipAddress);

        context.Sessions.Add(session);

        var issued = IssueCredential(session.Id, session.AbsoluteExpiresAt);

        await context.SaveChangesAsync(cancellationToken);

        return issued.Snapshot;
    }

    public async Task<SessionRenewalResult> RenewAsync(
        string renewalCredential,
        CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(renewalCredential))
        {
            return SessionRenewalResult.Failed("missing");
        }

        var now = clock.GetUtcNow();
        var hash = Hash(renewalCredential);

        var credential = await context.RenewalCredentials
            .FirstOrDefaultAsync(entry => entry.TokenHash == hash, cancellationToken);

        if (credential is null)
        {
            return SessionRenewalResult.Failed("unknown");
        }

        var session = await context.Sessions
            .FirstOrDefaultAsync(entry => entry.Id == credential.SessionId, cancellationToken);

        if (session is null)
        {
            return SessionRenewalResult.Failed("unknown");
        }

        // Reuse is compromise, not a race. Revoking everything is the only safe reading: either the
        // credential leaked, or the client is behaving in a way that makes rotation meaningless.
        if (credential.IsSpent)
        {
            session.Revoke(now, SessionRevocationReason.CredentialReused);
            await context.SaveChangesAsync(cancellationToken);

            return new SessionRenewalResult(false, session.Id, session.UserId, null, "reused");
        }

        if (credential.ExpiresAt <= now)
        {
            return SessionRenewalResult.Failed("expired");
        }

        if (!session.IsActive(now))
        {
            return SessionRenewalResult.Failed(session.IsRevoked ? "revoked" : "expired");
        }

        var replacement = IssueCredential(session.Id, session.AbsoluteExpiresAt);

        // Spending records which credential replaced this one, so a later reuse can be traced back
        // through the rotation chain to where the copy was made.
        credential.Spend(now, replacement.CredentialId);
        session.RecordActivity(now);

        await context.SaveChangesAsync(cancellationToken);

        return new SessionRenewalResult(true, session.Id, session.UserId, replacement.Snapshot, null);
    }

    public async Task<bool> IsActiveAsync(Guid sessionId, CancellationToken cancellationToken = default)
    {
        var now = clock.GetUtcNow();

        var session = await context.Sessions
            .AsNoTracking()
            .FirstOrDefaultAsync(entry => entry.Id == sessionId, cancellationToken);

        return session?.IsActive(now) == true;
    }

    public async Task RevokeAsync(Guid sessionId, string reason, CancellationToken cancellationToken = default)
    {
        var session = await context.Sessions
            .FirstOrDefaultAsync(entry => entry.Id == sessionId, cancellationToken);

        if (session is null)
        {
            return;
        }

        session.Revoke(clock.GetUtcNow(), reason);
        await context.SaveChangesAsync(cancellationToken);
    }

    public async Task<int> RevokeAllForUserAsync(
        Guid userId,
        string reason,
        CancellationToken cancellationToken = default)
    {
        var now = clock.GetUtcNow();

        var sessions = await context.Sessions
            .Where(entry => entry.UserId == userId && entry.RevokedAt == null)
            .ToListAsync(cancellationToken);

        foreach (var session in sessions)
        {
            session.Revoke(now, reason);
        }

        await context.SaveChangesAsync(cancellationToken);

        return sessions.Count;
    }

    private (SessionSnapshot Snapshot, Guid CredentialId) IssueCredential(Guid sessionId, DateTimeOffset expiresAt)
    {
        var value = GenerateCredential();
        var credential = RenewalCredential.Issue(sessionId, Hash(value), expiresAt);

        context.RenewalCredentials.Add(credential);

        return (new SessionSnapshot(sessionId, value, expiresAt), credential.Id);
    }

    /// <summary>256 bits from a cryptographic source - guessing one must be infeasible.</summary>
    private static string GenerateCredential() =>
        Convert.ToBase64String(RandomNumberGenerator.GetBytes(32))
            .Replace("+", "-", StringComparison.Ordinal)
            .Replace("/", "_", StringComparison.Ordinal)
            .TrimEnd('=');

    /// <summary>
    /// Only the hash is stored. A database leak must not hand over live sessions, and the value is
    /// high-entropy random rather than a password, so a fast hash is the right choice here.
    /// </summary>
    private static string Hash(string value) =>
        Convert.ToBase64String(SHA256.HashData(System.Text.Encoding.UTF8.GetBytes(value)));
}

/// <summary>Session lifetime settings, mirrored into Infrastructure (see <see cref="TokenIssuerSettings"/>).</summary>
public sealed class SessionSettings
{
    public int InactivityHours { get; init; } = 8;

    public int AbsoluteHours { get; init; } = 12;
}
