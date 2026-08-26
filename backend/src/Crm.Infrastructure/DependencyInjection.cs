using Crm.Application.Abstractions;
using Crm.Infrastructure.Auditing;
using Crm.Infrastructure.Persistence;
using Crm.Infrastructure.Persistence.Interceptors;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;

namespace Crm.Infrastructure;

/// <summary>
/// Composition for everything infrastructural. Keeping registration here means the API layer
/// never references a persistence or vendor type - a rule the architecture tests enforce
/// (Constitution I).
/// </summary>
public static class DependencyInjection
{
    /// <summary>Settings the API resolves from its own options and passes down.</summary>
    public sealed record PersistenceSettings(
        string ConnectionString,
        int CommandTimeoutSeconds,
        int MaxRetryCount);

    public static IServiceCollection AddCrmPersistence(
        this IServiceCollection services,
        Func<IServiceProvider, PersistenceSettings> settingsFactory)
    {
        ArgumentNullException.ThrowIfNull(services);
        ArgumentNullException.ThrowIfNull(settingsFactory);

        services.AddScoped<IAuditRecorder, LoggingAuditRecorder>();
        services.AddScoped<AuditingSaveChangesInterceptor>();

        services.AddDbContext<CrmDbContext>((serviceProvider, options) =>
        {
            var settings = settingsFactory(serviceProvider);

            options.UseSqlServer(settings.ConnectionString, sql =>
            {
                sql.EnableRetryOnFailure(settings.MaxRetryCount);
                sql.CommandTimeout(settings.CommandTimeoutSeconds);
                sql.MigrationsAssembly(typeof(CrmDbContext).Assembly.FullName);
            });

            options.AddInterceptors(serviceProvider.GetRequiredService<AuditingSaveChangesInterceptor>());
        });

        return services;
    }

    /// <summary>
    /// Applies pending migrations. Exposed here so the API can trigger it without referencing
    /// EF Core itself.
    /// </summary>
    public static async Task MigrateAsync(IServiceProvider services, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(services);

        var context = services.GetRequiredService<CrmDbContext>();
        await context.Database.MigrateAsync(cancellationToken);
    }

    /// <summary>Reports whether the database is reachable, without exposing EF Core to callers.</summary>
    public static async Task<bool> CanConnectAsync(
        IServiceProvider services,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(services);

        var context = services.GetRequiredService<CrmDbContext>();
        return await context.Database.CanConnectAsync(cancellationToken);
    }
}
