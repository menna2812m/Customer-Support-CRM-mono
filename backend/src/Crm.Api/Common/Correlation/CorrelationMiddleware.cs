using System.Diagnostics;
using Crm.Api.Configuration;
using Microsoft.Extensions.Options;

namespace Crm.Api.Common.Correlation;

/// <summary>
/// Establishes the correlation identifier for every request. Runs first in the pipeline so that
/// even a failure in later middleware still has an identifier to report.
///
/// Precedence (spec FR-041): a caller-supplied header wins, otherwise the current activity trace
/// identifier is reused so correlation lines up with any future distributed tracing.
/// </summary>
public sealed class CorrelationMiddleware(RequestDelegate next, IOptions<ObservabilityOptions> options)
{
    private readonly string _headerName = options.Value.CorrelationHeader;

    public async Task InvokeAsync(HttpContext context, ICorrelationContext correlation)
    {
        ArgumentNullException.ThrowIfNull(context);
        ArgumentNullException.ThrowIfNull(correlation);

        var id = ResolveId(context);

        if (correlation is CorrelationContext writable)
        {
            writable.Set(id);
        }

        // Make the value queryable from logs written by any component in this request.
        Activity.Current?.SetTag("crm.correlation_id", id);

        context.Response.OnStarting(() =>
        {
            context.Response.Headers[_headerName] = id;
            return Task.CompletedTask;
        });

        using var scope = context.RequestServices
            .GetRequiredService<ILoggerFactory>()
            .CreateLogger("Crm.Correlation")
            .BeginScope(new Dictionary<string, object> { ["CorrelationId"] = id });

        await next(context);
    }

    private string ResolveId(HttpContext context)
    {
        if (context.Request.Headers.TryGetValue(_headerName, out var supplied))
        {
            var candidate = supplied.ToString();

            // Bound the value: it is echoed into responses and written to logs.
            if (!string.IsNullOrWhiteSpace(candidate) && candidate.Length <= 128)
            {
                return candidate;
            }
        }

        return Activity.Current?.TraceId.ToString() ?? Guid.CreateVersion7().ToString("n");
    }
}
