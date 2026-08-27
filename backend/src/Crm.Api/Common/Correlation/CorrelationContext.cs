using System.Diagnostics;

namespace Crm.Api.Common.Correlation;

/// <summary>
/// Carries the correlation identifier for the current request so logs, error responses, and audit
/// records all quote the same value (spec FR-041).
/// </summary>
public interface ICorrelationContext
{
    string Id { get; }
}

internal sealed class CorrelationContext : ICorrelationContext
{
    private string? _id;

    public string Id => _id ??= Activity.Current?.TraceId.ToString() ?? Guid.CreateVersion7().ToString("n");

    public void Set(string id) => _id = id;
}
