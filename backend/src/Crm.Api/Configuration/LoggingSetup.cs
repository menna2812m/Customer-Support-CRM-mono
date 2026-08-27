using Crm.Api.Common.Correlation;
using Crm.Application.Abstractions;
using Serilog;
using Serilog.Events;
using Serilog.Formatting.Compact;

namespace Crm.Api.Configuration;

/// <summary>
/// Structured logging (Constitution XI, spec FR-040 to FR-043).
///
/// Production runs under IIS, so logs must land on disk in a machine-readable form - console
/// capture is not available there. Verbosity comes from configuration, so changing it in an
/// incident does not require a deployment.
///
/// Redaction is applied at the sink boundary rather than at each call site, because the one call
/// site that forgets is the one that leaks (FR-042).
/// </summary>
public static class LoggingSetup
{
    /// <summary>
    /// Property and member names whose values must never reach a log. Matching is
    /// case-insensitive and by substring, so <c>ConnectionString</c> and <c>db_connection_string</c>
    /// are both covered.
    /// </summary>
    public static readonly string[] SensitiveNames =
    [
        "password",
        "token",
        "secret",
        "authorization",
        "connectionstring",
        "apikey",
        "clientsecret",
        "credential",
        "renewal",
        "sessionid",
        "refresh",
    ];

    public const string RedactedValue = "[redacted]";

    public static IHostBuilder UseCrmSerilog(this IHostBuilder host)
    {
        ArgumentNullException.ThrowIfNull(host);

        return host.UseSerilog((context, services, configuration) =>
        {
            var observability = context.Configuration
                .GetSection(ObservabilityOptions.SectionName)
                .Get<ObservabilityOptions>() ?? new ObservabilityOptions();

            configuration
                .ReadFrom.Configuration(context.Configuration)
                .ReadFrom.Services(services)
                .Enrich.FromLogContext()
                .Enrich.WithProperty("Application", "Crm.Api")
                .Enrich.With(new RedactingEnricher())
                .WriteTo.File(
                    new CompactJsonFormatter(),
                    observability.LogFilePath,
                    rollingInterval: RollingInterval.Day,
                    retainedFileCountLimit: observability.RetainedFileCount,
                    fileSizeLimitBytes: 64 * 1024 * 1024,
                    rollOnFileSizeLimit: true,
                    shared: true);

            if (context.HostingEnvironment.IsDevelopment())
            {
                configuration.WriteTo.Console(formatProvider: System.Globalization.CultureInfo.InvariantCulture);
            }
        });
    }

    /// <summary>
    /// Adds request context to every log entry so an operator can move from a user-visible
    /// correlation identifier to the whole story of that request (spec SC-005).
    /// </summary>
    public static IApplicationBuilder UseCrmRequestLogging(this IApplicationBuilder app)
    {
        ArgumentNullException.ThrowIfNull(app);

        return app.UseSerilogRequestLogging(options =>
        {
            options.GetLevel = static (httpContext, elapsed, exception) =>
                exception is not null || httpContext.Response.StatusCode >= 500
                    ? LogEventLevel.Error
                    : LogEventLevel.Information;

            options.EnrichDiagnosticContext = static (diagnostic, httpContext) =>
            {
                var services = httpContext.RequestServices;

                diagnostic.Set("CorrelationId", services.GetRequiredService<ICorrelationContext>().Id);
                diagnostic.Set("RequestPath", httpContext.Request.Path.Value);

                var user = services.GetRequiredService<ICurrentUser>();

                if (user.IsAuthenticated)
                {
                    diagnostic.Set("UserId", user.UserId);
                    diagnostic.Set("Population", user.Population?.ToString());
                }
            };
        });
    }
}
