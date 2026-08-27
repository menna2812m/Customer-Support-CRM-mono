using Asp.Versioning;
using Crm.Application.Common;
using Microsoft.AspNetCore.Mvc;

namespace Crm.Api.Configuration;

/// <summary>
/// URL-segment API versioning (Constitution III): every application endpoint lives under
/// <c>/api/v{version}</c>. Operational endpoints - health probes and the development-only
/// OpenAPI document - are deliberately exempt (spec FR-015).
/// </summary>
public static class ApiVersioningSetup
{
    public static IServiceCollection AddCrmApiVersioning(this IServiceCollection services)
    {
        ArgumentNullException.ThrowIfNull(services);

        services
            .AddApiVersioning(options =>
            {
                options.DefaultApiVersion = new ApiVersion(1, 0);
                options.AssumeDefaultVersionWhenUnspecified = false;
                options.ReportApiVersions = true;
                options.ApiVersionReader = new UrlSegmentApiVersionReader();
            })
            .AddMvc()
            .AddApiExplorer(options =>
            {
                options.GroupNameFormat = "'v'VVV";
                options.SubstituteApiVersionInUrl = true;
            });

        return services;
    }
}
