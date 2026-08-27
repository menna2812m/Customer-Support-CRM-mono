using Crm.Api.Common.Correlation;
using Crm.Api.Common.Errors;
using Crm.Api.Common.Security;
using Crm.Api.Common.Validation;
using Crm.Api.Configuration;
using Crm.Api.Diagnostics;
using Crm.Application.Abstractions;
using Crm.Application.Common;
using Crm.Infrastructure;
using Crm.Infrastructure.Configuration;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Authorization.Policy;
using Microsoft.Extensions.Options;

var builder = WebApplication.CreateBuilder(args);

// Configuration layers: settings files, environment variables (added by the host), then the
// host-side protected store. Secrets never live in the published folder (spec FR-008).
builder.Configuration.AddCrmSecrets();

// Structured logging to a durable destination: production runs under IIS, where console capture
// is not available (spec FR-040).
builder.Host.UseCrmSerilog();

// Spec FR-055: bound the request body centrally, for both Kestrel and IIS.
builder.WebHost.ConfigureKestrel(kestrel => kestrel.Limits.MaxRequestBodySize = CrmRequestLimits.MaxBodyBytes);
builder.Services.Configure<IISServerOptions>(iis => iis.MaxRequestBodySize = CrmRequestLimits.MaxBodyBytes);

builder.Services.AddCrmOptions();
builder.Services.AddCrmErrorContract();
builder.Services.AddCrmApiVersioning();
builder.Services.AddCrmCors();
builder.Services.AddCrmHealthChecks();
builder.Services.AddCrmOpenApi();
builder.Services.AddCrmValidation();
builder.Services.AddCrmRateLimiting();
builder.Services.AddControllers()
    .AddJsonOptions(options =>
    {
        // Bound the shape of what we will parse (spec FR-055): depth 32, with the body size and
        // collection length limits enforced alongside it.
        options.JsonSerializerOptions.MaxDepth = CrmRequestLimits.MaxJsonDepth;
        options.JsonSerializerOptions.PropertyNamingPolicy = System.Text.Json.JsonNamingPolicy.CamelCase;
        options.JsonSerializerOptions.DefaultIgnoreCondition =
            System.Text.Json.Serialization.JsonIgnoreCondition.WhenWritingNull;
        options.JsonSerializerOptions.Converters.Add(
            new System.Text.Json.Serialization.JsonStringEnumConverter());
    });
builder.Services.AddExceptionHandler<GlobalExceptionHandler>();

builder.Services.AddHttpContextAccessor();
builder.Services.AddSingleton(TimeProvider.System);
builder.Services.AddScoped<ICorrelationContext, CorrelationContext>();
builder.Services.AddScoped<ICurrentUser, CurrentUser>();

// Authentication composition - the single shared registration point this feature touches.
builder.Services.AddScoped<Crm.Application.Identity.StaffSignIn>();
builder.Services.AddScoped<Crm.Application.Identity.DeactivateUser>();
builder.Services.AddScoped<Crm.Api.Auth.AuthCookies>();
builder.Services.AddScoped<Crm.Infrastructure.Identity.ICorrelationAccessor, Crm.Api.Auth.HttpCorrelationAccessor>();

// Reference slice registration - the single shared registration point a feature touches (SC-002).
builder.Services.AddScoped<Crm.Application.Diagnostics.DiagnosticItemQuery>();

// Authentication and authorization seams. Deny by default: anything without an explicit
// [AllowAnonymous] requires an authenticated caller (Constitution IV, spec FR-025).
builder.Services.AddCrmAuthentication();

builder.Services.AddSingleton<IAuthorizationPolicyProvider, CrmAuthorizationPolicyProvider>();
builder.Services.AddScoped<IAuthorizationHandler, PermissionAuthorizationHandler>();
builder.Services.AddScoped<IAuthorizationHandler, PopulationAuthorizationHandler>();
builder.Services.AddSingleton<IAuthorizationMiddlewareResultHandler, AuthorizationFailureLogger>();
builder.Services.AddAuthorizationBuilder()
    .SetFallbackPolicy(new AuthorizationPolicyBuilder().RequireAuthenticatedUser().Build());

// Identity composition lives in Crm.Infrastructure alongside persistence, for the same reason: the
// API layer references neither the provider client nor the token library.
//
// The delegates below read configuration when the options are first resolved, not while services
// are being registered. That distinction cost an afternoon in feature 001: an eager read misses
// anything layered in afterwards, which is exactly how a deployment override or a test host
// supplies settings.
builder.Services.AddCrmIdentity(
    provider => builder.Configuration.GetSection("Authentication:Staff").Bind(provider),
    token => builder.Configuration.GetSection(TokenOptions.SectionName).Bind(token),
    session => builder.Configuration.GetSection(CrmSessionOptions.SectionName).Bind(session));

// Persistence is composed inside Crm.Infrastructure so that no API type references EF Core or a
// database driver - an architecture test enforces this (Constitution I).
builder.Services.AddCrmPersistence(serviceProvider =>
{
    var database = serviceProvider.GetRequiredService<IOptions<DatabaseOptions>>().Value;

    return new DependencyInjection.PersistenceSettings(
        database.ConnectionString,
        database.CommandTimeoutSeconds,
        database.MaxRetryCount);
});

var app = builder.Build();

// Fail fast with one message naming every configuration problem (spec FR-007).
app.Services.ValidateCrmConfiguration(app.Environment);

app.UseMiddleware<CorrelationMiddleware>();
app.UseMiddleware<SecurityHeadersMiddleware>();
app.UseCrmRequestLogging();

if (!app.Environment.IsDevelopment())
{
    // Spec FR-052. Development runs over plain HTTP so the frontend dev server can reach the API
    // without a certificate dance.
    app.UseHsts();
    app.UseHttpsRedirection();
}
app.UseExceptionHandler();

// Give framework-generated failures (unmatched routes, auth challenges) the same contract.
app.UseStatusCodePages(async statusCodeContext =>
{
    var context = statusCodeContext.HttpContext;
    var status = context.Response.StatusCode;

    await ErrorContractSetup.WriteProblemAsync(
        context,
        status,
        ErrorContractSetup.CodeForStatus(status),
        "The request could not be completed.");
});

app.UseCors(CorsOptions.PolicyName);

// After CORS so a preflight is never throttled, and before authentication so an anonymous flood
// is refused before it costs a database round trip (spec FR-036).
app.UseRateLimiter();

// An unknown version segment is answered explicitly rather than falling through to a bare 404.
app.UseCrmApiVersionGuard("v1");

app.UseAuthentication();
app.UseAuthorization();

app.MapControllers();
app.MapCrmHealthEndpoints();
app.MapCrmOpenApi();

// Unmatched routes answer with the shared contract. Two details matter here: the fallback is
// anonymous, because otherwise deny-by-default turns every unknown path into a 401 that tells an
// integrator nothing; and the pattern is explicit, because the default fallback pattern skips
// file-like paths - which would leave /openapi/v1.json answering 401 in production.
app.MapFallback("/{**path}", async context => await ErrorContractSetup.WriteProblemAsync(
        context,
        StatusCodes.Status404NotFound,
        ErrorCodes.NotFound,
        "The requested resource was not found."))
    .AllowAnonymous();

await MigrateIfConfiguredAsync(app);

// Spec FR-024: a stored grant naming a permission the catalog no longer declares is reported here
// rather than silently granting nothing.
await app.Services.ValidateSeededPermissionsAsync();

await app.RunAsync();

static async Task MigrateIfConfiguredAsync(WebApplication app)
{
    var database = app.Services.GetRequiredService<IOptions<DatabaseOptions>>().Value;

    // Spec FR-013: development convenience only. Startup validation rejects it elsewhere.
    if (!database.AutoMigrateOnStartup || !app.Environment.IsDevelopment())
    {
        return;
    }

    using var scope = app.Services.CreateScope();
    var logger = scope.ServiceProvider.GetRequiredService<ILogger<Program>>();

    try
    {
        await DependencyInjection.MigrateAsync(scope.ServiceProvider);
    }
    catch (Exception ex)
    {
        // Spec FR-010: an unreachable database is reported as an unhealthy dependency and a
        // structured log entry - not an unhandled crash. The application still starts, so
        // /health/ready can say what is wrong instead of the host restart-looping silently.
        logger.LogError(
            ex,
            "Startup migration could not be applied. The application will start, and readiness "
                + "will report the database as unhealthy until it is reachable.");
    }
}

/// <summary>Exposed so the integration test host can reference this entry point.</summary>
public partial class Program;
