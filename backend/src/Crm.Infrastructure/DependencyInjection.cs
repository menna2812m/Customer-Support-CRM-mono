using System.Net.Http;
using Crm.Application.Abstractions;
using Crm.Infrastructure.Auditing;
using Crm.Infrastructure.Identity;
using Crm.Infrastructure.Organization;
using Crm.Infrastructure.Persistence;
using Crm.Infrastructure.Persistence.Interceptors;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Microsoft.Extensions.Options;
using Microsoft.IdentityModel.Protocols;
using Microsoft.IdentityModel.Protocols.OpenIdConnect;

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
    /// Identity composition (feature 002). Registered here so that no API type references the
    /// provider client, the token library, or the persistence types - the rule the architecture
    /// tests enforce.
    /// </summary>
    public static IServiceCollection AddCrmIdentity(
        this IServiceCollection services,
        Action<ProviderSettings> configureProvider,
        Action<TokenIssuerSettings> configureToken,
        Action<SessionSettings> configureSession)
    {
        ArgumentNullException.ThrowIfNull(services);
        ArgumentNullException.ThrowIfNull(configureProvider);
        ArgumentNullException.ThrowIfNull(configureToken);
        ArgumentNullException.ThrowIfNull(configureSession);

        services.Configure(configureProvider);
        services.Configure(configureToken);
        services.Configure(configureSession);

        services.AddScoped<ITokenIssuer, TokenIssuer>();
        services.AddScoped<IIdentityStore, IdentityStore>();
        services.AddScoped<IOrganizationStore, OrganizationStore>();

        // Feature 004. Separate from IIdentityStore: sign-in asks who an arriving identity is,
        // administration asks who exists and what they may do.
        services.AddScoped<IPeopleStore, PeopleStore>();
        services.AddScoped<IAuthenticationEventLog, AuthenticationEventLog>();
        services.AddScoped<ISessionStore, SessionStore>();

        // The discovery document is cached and refreshed by the manager rather than fetched per
        // sign-in; a provider that is briefly slow must not make every sign-in slow.
        services.TryAddSingleton<IConfigurationManager<OpenIdConnectConfiguration>>(provider =>
        {
            var settings = provider.GetRequiredService<IOptions<ProviderSettings>>().Value;
            var authority = settings.Authority?.TrimEnd('/') ?? string.Empty;

            return new ConfigurationManager<OpenIdConnectConfiguration>(
                $"{authority}/.well-known/openid-configuration",
                new OpenIdConnectConfigurationRetriever(),
                new HttpDocumentRetriever(provider.GetRequiredService<IHttpClientFactory>().CreateClient(ProviderHttpClient))
                {
                    RequireHttps = !authority.StartsWith("http://localhost", StringComparison.OrdinalIgnoreCase),
                });
        });

        services
            .AddHttpClient<IIdentityProviderClient, OpenIdConnectClient>(ProviderHttpClient)
            .ConfigureHttpClient(client => client.Timeout = TimeSpan.FromSeconds(8));

        return services;
    }

    /// <summary>Named client so the provider timeout is bounded and observable (spec SC-011).</summary>
    public const string ProviderHttpClient = "crm-identity-provider";

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
