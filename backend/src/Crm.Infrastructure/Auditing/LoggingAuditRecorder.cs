using Crm.Application.Abstractions;
using Microsoft.Extensions.Logging;

namespace Crm.Infrastructure.Auditing;

/// <summary>
/// Writes audit records as structured log entries (spec FR-028).
///
/// This is the shipped implementation of the seam, not the final destination: the future audit-log
/// feature replaces it with persistence, and no call site changes. Entries carry the correlation
/// identifier so an audit record and the request that produced it can be read together.
/// </summary>
public sealed class LoggingAuditRecorder(ILogger<LoggingAuditRecorder> logger) : IAuditRecorder
{
    public Task RecordAsync(AuditEntry entry, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(entry);

        if (!logger.IsEnabled(LogLevel.Information))
        {
            return Task.CompletedTask;
        }

        logger.LogInformation(
            "Audit {AuditAction} by {ActorId} on {TargetType}/{TargetId} at {OccurredAt} "
                + "(correlation {CorrelationId}) {@AuditMetadata}",
            entry.Action,
            entry.ActorId,
            entry.TargetType,
            entry.TargetId,
            entry.OccurredAt,
            entry.CorrelationId,
            entry.Metadata);

        return Task.CompletedTask;
    }
}
