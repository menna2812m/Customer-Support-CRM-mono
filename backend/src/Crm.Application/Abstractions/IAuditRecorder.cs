namespace Crm.Application.Abstractions;

/// <summary>
/// A security-sensitive action worth recording (Constitution IV, spec FR-028).
/// </summary>
/// <param name="Action">Stable identifier, for example <c>auth.login.failed</c>.</param>
/// <param name="ActorId">Acting user, or null for an anonymous attempt.</param>
/// <param name="TargetType">What kind of thing was acted on, when applicable.</param>
/// <param name="TargetId">Which one.</param>
/// <param name="OccurredAt">When, from the injected clock rather than the wall clock.</param>
/// <param name="CorrelationId">Ties the record to this request's log entries.</param>
/// <param name="Metadata">
/// Extra context. Must contain no secret or sensitive value - the redaction rules apply here too
/// (spec FR-042).
/// </param>
public sealed record AuditEntry(
    string Action,
    Guid? ActorId,
    string? TargetType,
    string? TargetId,
    DateTimeOffset OccurredAt,
    string CorrelationId,
    IReadOnlyDictionary<string, string>? Metadata = null);

/// <summary>
/// Writing surface for audit records. This feature ships a structured-logging implementation; the
/// future audit-log feature persists entries without any call site changing.
/// </summary>
public interface IAuditRecorder
{
    Task RecordAsync(AuditEntry entry, CancellationToken cancellationToken = default);
}
