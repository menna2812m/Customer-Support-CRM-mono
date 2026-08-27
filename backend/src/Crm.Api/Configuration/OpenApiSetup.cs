using Microsoft.OpenApi;
using Scalar.AspNetCore;

namespace Crm.Api.Configuration;

/// <summary>
/// Machine-readable API documentation (spec FR-022), served in Development only.
///
/// AR-002 forbids anonymous exposure elsewhere, and an integration test asserts that both the
/// document and the UI return 404 outside Development.
/// </summary>
public static class OpenApiSetup
{
    public static IServiceCollection AddCrmOpenApi(this IServiceCollection services)
    {
        ArgumentNullException.ThrowIfNull(services);

        services.AddOpenApi(options =>
        {
            options.AddDocumentTransformer((document, _, _) =>
            {
                document.Info = new OpenApiInfo
                {
                    Title = "Customer Support CRM API",
                    Version = "v1",
                    Description =
                        "Application endpoints are versioned under /api/v1. Health probes are "
                            + "operational endpoints and sit outside the version segment.",
                };

                document.Components ??= new OpenApiComponents();
                document.Components.SecuritySchemes = new Dictionary<string, IOpenApiSecurityScheme>
                {
                    ["staffBearer"] = new OpenApiSecurityScheme
                    {
                        Type = SecuritySchemeType.Http,
                        Scheme = "bearer",
                        BearerFormat = "JWT",
                        Description = "Staff population - token issued by the corporate identity provider.",
                    },
                    ["portalBearer"] = new OpenApiSecurityScheme
                    {
                        Type = SecuritySchemeType.Http,
                        Scheme = "bearer",
                        BearerFormat = "JWT",
                        Description = "Portal population - token issued by the CRM for external accounts.",
                    },
                };

                return Task.CompletedTask;
            });
        });

        return services;
    }

    public static WebApplication MapCrmOpenApi(this WebApplication app)
    {
        ArgumentNullException.ThrowIfNull(app);

        if (!app.Environment.IsDevelopment())
        {
            return app;
        }

        app.MapOpenApi().AllowAnonymous();
        app.MapScalarApiReference(options => options.WithTitle("Customer Support CRM API"))
            .AllowAnonymous();

        return app;
    }
}
