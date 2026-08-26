using Crm.Infrastructure;
using Microsoft.Extensions.Diagnostics.HealthChecks;

namespace Crm.Api.Diagnostics;

/// <summary>
/// Reports whether the relational database is reachable.
///
/// The result is cached briefly (<see cref="CacheDuration"/>) so an anonymous endpoint cannot be
/// used to hammer the database, while staying well inside the 30-second reporting window required
/// by spec SC-006.
/// </summary>
public sealed class DatabaseHealthCheck(
    IServiceProvider services,
    HealthCheckCache cache,
    TimeProvider clock,
    ILogger<DatabaseHealthCheck> logger) : IHealthCheck
{
    public static readonly TimeSpan CacheDuration = TimeSpan.FromSeconds(5);

    public const string Name = "database";

    public async Task<HealthCheckResult> CheckHealthAsync(
        HealthCheckContext context,
        CancellationToken cancellationToken = default)
    {
        var now = clock.GetUtcNow();

        if (cache.TryGet(Name, now, CacheDuration, out var cached))
        {
            return cached;
        }

        HealthCheckResult result;

        try
        {
            var reachable = await DependencyInjection.CanConnectAsync(services, cancellationToken);

            result = reachable
                ? HealthCheckResult.Healthy()
                : HealthCheckResult.Unhealthy("The database is not reachable.");
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            // The exception detail goes to the log; the response says only that it is unhealthy,
            // because the endpoint is anonymous (spec FR-044, AR-001).
            logger.LogError(ex, "Database health check failed.");
            result = HealthCheckResult.Unhealthy("The database is not reachable.");
        }

        cache.Set(Name, now, result);
        return result;
    }
}

/// <summary>Process-wide store for the most recent result of each dependency check.</summary>
public sealed class HealthCheckCache
{
    private readonly Lock _gate = new();
    private readonly Dictionary<string, (DateTimeOffset At, HealthCheckResult Result)> _entries = [];

    public bool TryGet(string name, DateTimeOffset now, TimeSpan duration, out HealthCheckResult result)
    {
        lock (_gate)
        {
            if (_entries.TryGetValue(name, out var entry) && now - entry.At < duration)
            {
                result = entry.Result;
                return true;
            }
        }

        result = default;
        return false;
    }

    public void Set(string name, DateTimeOffset at, HealthCheckResult result)
    {
        lock (_gate)
        {
            _entries[name] = (at, result);
        }
    }
}
