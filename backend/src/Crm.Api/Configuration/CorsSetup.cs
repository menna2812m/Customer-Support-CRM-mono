using Microsoft.Extensions.Options;
using AspNetCorsOptions = Microsoft.AspNetCore.Cors.Infrastructure.CorsOptions;

namespace Crm.Api.Configuration;

/// <summary>
/// Cross-origin access from the configured allowlist (spec FR-054).
///
/// This feature wires the development frontend origin; task T108 generalizes the policy across
/// environments and rejects a wildcard configuration at startup. Nothing else in the application
/// touches CORS.
/// </summary>
public static class CorsSetup
{
    public static IServiceCollection AddCrmCors(this IServiceCollection services)
    {
        ArgumentNullException.ThrowIfNull(services);

        services.AddCors();
        services.AddSingleton<IConfigureOptions<AspNetCorsOptions>, ConfigureCrmCorsPolicy>();

        return services;
    }
}

/// <summary>
/// Builds the named policy from configuration. Implemented as a configure-options service so the
/// allowlist is read from the real container rather than from a second one built during startup.
/// </summary>
internal sealed class ConfigureCrmCorsPolicy(IOptions<CorsOptions> crmOptions)
    : IConfigureOptions<AspNetCorsOptions>
{
    public void Configure(AspNetCorsOptions options)
    {
        ArgumentNullException.ThrowIfNull(options);

        var allowedOrigins = crmOptions.Value.AllowedOrigins.ToArray();

        options.AddPolicy(CorsOptions.PolicyName, policy =>
        {
            policy
                .WithOrigins(allowedOrigins)
                .AllowAnyHeader()
                .AllowAnyMethod()

                // The renewal cookie is what lets a reload restore a session, and a browser sends
                // no cookie cross-origin without this. Safe here only because the origins are an
                // explicit allowlist - startup rejects a wildcard, which this would otherwise make
                // exploitable.
                .AllowCredentials()
                .WithExposedHeaders("X-Correlation-Id");
        });
    }
}
