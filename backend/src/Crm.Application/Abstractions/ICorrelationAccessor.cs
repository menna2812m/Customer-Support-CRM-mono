namespace Crm.Application.Abstractions;

/// <summary>
/// Request context that auditing needs. Implemented in the API layer, where the correlation
/// identifier and the caller's address live, so neither Application nor Infrastructure needs an
/// HTTP dependency.
/// </summary>
/// <remarks>
/// Declared here rather than in Infrastructure because both layers consume it: feature 002's
/// authentication event log and feature 003's organization auditing. An abstraction used by
/// Application belongs beside <see cref="IAuditRecorder"/>, not below it.
/// </remarks>
public interface ICorrelationAccessor
{
    string CorrelationId { get; }

    string? IpAddress { get; }
}
