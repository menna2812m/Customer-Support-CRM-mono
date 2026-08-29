using Crm.Application.Abstractions;
using Crm.Domain.Identity;
using Crm.Infrastructure.Persistence;
using Microsoft.Extensions.Logging;

namespace Crm.Infrastructure.Identity;

/// <summary>
/// Persists authentication events and mirrors them to the audit recorder (spec FR-039).
///
/// Two destinations on purpose: the log carries the request context an operator needs while
/// investigating, and the table answers "what happened to this account" without anyone reading log
/// files. Neither ever receives a credential, token, or hash.
/// </summary>
public sealed class AuthenticationEventLog(
    CrmDbContext context,
    IAuditRecorder auditRecorder,
    ICorrelationAccessor correlation,
    TimeProvider clock,
    ILogger<AuthenticationEventLog> logger) : IAuthenticationEventLog
{
    public Task RecordSuccessAsync(Guid userId, string providerSubject, CancellationToken cancellationToken = default) =>
        WriteAsync(
            AuthenticationActions.SignInSucceeded,
            AuthenticationOutcomes.Succeeded,
            userId,
            providerSubject,
            detail: null,
            cancellationToken);

    public Task RecordRefusalAsync(
        string reason,
        string providerSubject,
        Guid? userId,
        CancellationToken cancellationToken = default) =>
        WriteAsync(
            AuthenticationActions.SignInRefused,
            AuthenticationOutcomes.Refused,
            userId,
            providerSubject,
            detail: reason,
            cancellationToken);

    public Task RecordCollisionAsync(
        string providerSubject,
        Guid existingUserId,
        CancellationToken cancellationToken = default) =>
        WriteAsync(
            AuthenticationActions.SignInCollision,
            AuthenticationOutcomes.Refused,
            existingUserId,
            providerSubject,
            detail: "An unknown subject presented an email already held by this user.",
            cancellationToken);

    public Task RecordRoleGrantAsync(
        Guid userId,
        string roleName,
        string grantedBecause,
        CancellationToken cancellationToken = default) =>
        WriteAsync(
            AuthenticationActions.RoleGranted,
            AuthenticationOutcomes.Succeeded,
            userId,
            subjectReference: null,
            detail: $"{roleName} ({grantedBecause})",
            cancellationToken);

    public Task RecordSessionRevokedAsync(
        Guid userId,
        Guid sessionId,
        string reason,
        CancellationToken cancellationToken = default) =>
        WriteAsync(
            reason == SessionRevocationReason.CredentialReused
                ? AuthenticationActions.CredentialReused
                : AuthenticationActions.SessionRevoked,
            AuthenticationOutcomes.Succeeded,
            userId,
            subjectReference: null,
            detail: reason,
            cancellationToken,
            sessionId);

    public Task RecordSessionRenewedAsync(
        Guid userId,
        Guid sessionId,
        CancellationToken cancellationToken = default) =>
        WriteAsync(
            AuthenticationActions.SessionRenewed,
            AuthenticationOutcomes.Succeeded,
            userId,
            subjectReference: null,
            detail: null,
            cancellationToken,
            sessionId);

    public Task RecordUserDeactivatedAsync(
        Guid userId,
        int revokedSessions,
        CancellationToken cancellationToken = default) =>
        WriteAsync(
            AuthenticationActions.SessionRevoked,
            AuthenticationOutcomes.Succeeded,
            userId,
            subjectReference: null,
            detail: $"{SessionRevocationReason.UserDeactivated} ({revokedSessions} session(s))",
            cancellationToken);

    private async Task WriteAsync(
        string action,
        string outcome,
        Guid? userId,
        string? subjectReference,
        string? detail,
        CancellationToken cancellationToken,
        Guid? sessionId = null)
    {
        var now = clock.GetUtcNow();
        var correlationId = correlation.CorrelationId;

        context.AuthenticationEvents.Add(AuthenticationEvent.Record(
            action,
            outcome,
            now,
            correlationId,
            userId,
            subjectReference,
            sessionId,
            correlation.IpAddress,
            detail));

        await context.SaveChangesAsync(cancellationToken);

        if (logger.IsEnabled(LogLevel.Information))
        {
            logger.LogInformation(
                "Authentication {AuthAction} {AuthOutcome} for user {UserId} (correlation {CorrelationId})",
                action,
                outcome,
                userId,
                correlationId);
        }

        await auditRecorder.RecordAsync(
            new AuditEntry(
                action,
                userId,
                "user",
                userId?.ToString(),
                now,
                correlationId,
                detail is null ? null : new Dictionary<string, string> { ["detail"] = detail }),
            cancellationToken);
    }
}

