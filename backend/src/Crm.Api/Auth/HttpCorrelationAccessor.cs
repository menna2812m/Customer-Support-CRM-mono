using Crm.Api.Common.Correlation;
using Crm.Infrastructure.Identity;

namespace Crm.Api.Auth;

/// <summary>
/// Supplies request context to the authentication event log, which lives in Infrastructure and so
/// cannot reach into HTTP itself.
/// </summary>
public sealed class HttpCorrelationAccessor(
    ICorrelationContext correlation,
    IHttpContextAccessor accessor) : ICorrelationAccessor
{
    public string CorrelationId => correlation.Id;

    public string? IpAddress => accessor.HttpContext?.Connection.RemoteIpAddress?.ToString();
}
