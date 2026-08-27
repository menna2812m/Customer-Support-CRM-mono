using System.Text.Json;
using Microsoft.AspNetCore.Diagnostics.HealthChecks;
using Microsoft.Extensions.Diagnostics.HealthChecks;

namespace Crm.Api.Diagnostics;

/// <summary>
/// Operational endpoints (spec FR-044, AR-001):
///
///   /health/live  - is the process running
///   /health/ready - are its dependencies usable
///
/// Both are anonymous and expose status only. They sit outside <c>/api/v1</c> deliberately: they
/// are consumed by hosting and monitoring rather than by application clients, and versioning them
/// would break the hosting contract on every version bump (spec FR-015).
/// </summary>
public static class HealthEndpoints
{
    private const string ReadyTag = "ready";

    public static IServiceCollection AddCrmHealthChecks(this IServiceCollection services)
    {
        ArgumentNullException.ThrowIfNull(services);

        services.AddSingleton<HealthCheckCache>();
        services
            .AddHealthChecks()
            .AddCheck<DatabaseHealthCheck>(
                DatabaseHealthCheck.Name,
                failureStatus: HealthStatus.Unhealthy,
                tags: [ReadyTag],
                timeout: TimeSpan.FromSeconds(5));

        return services;
    }

    public static WebApplication MapCrmHealthEndpoints(this WebApplication app)
    {
        ArgumentNullException.ThrowIfNull(app);

        app.MapHealthChecks("/health/live", new HealthCheckOptions
        {
            // Liveness must not depend on anything external: a database outage should not cause
            // the host to recycle a perfectly healthy process.
            Predicate = _ => false,
            ResponseWriter = WriteReportAsync,
        }).AllowAnonymous();

        app.MapHealthChecks("/health/ready", new HealthCheckOptions
        {
            Predicate = registration => registration.Tags.Contains(ReadyTag),
            ResponseWriter = WriteReportAsync,
        }).AllowAnonymous();

        return app;
    }

    /// <summary>
    /// Emits status, per-check status, and duration - and nothing else. Exception text, server
    /// names, and connection strings never appear, because these endpoints are anonymous.
    /// </summary>
    private static async Task WriteReportAsync(HttpContext context, HealthReport report)
    {
        context.Response.ContentType = "application/json; charset=utf-8";

        var payload = new
        {
            status = report.Status.ToString(),
            checks = report.Entries.Select(entry => new
            {
                name = entry.Key,
                status = entry.Value.Status.ToString(),
                durationMs = Math.Round(entry.Value.Duration.TotalMilliseconds, 1),
            }),
        };

        await context.Response.WriteAsync(JsonSerializer.Serialize(payload, JsonOptions));
    }

    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web);
}
