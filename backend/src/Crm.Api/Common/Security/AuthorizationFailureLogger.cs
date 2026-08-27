using Crm.Api.Common.Correlation;
using Crm.Application.Abstractions;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Authorization.Policy;

namespace Crm.Api.Common.Security;

/// <summary>
/// Records every authentication and authorization refusal (spec AR-007, Constitution IV).
///
/// The entry carries the correlation identifier and the attempted operation, and never the
/// submitted credentials - a log of failed sign-ins containing the passwords people tried is a
/// breach waiting to be discovered (FR-042).
///
/// The handler is a singleton, so per-request services are resolved from the request scope rather
/// than injected.
/// </summary>
public sealed class AuthorizationFailureLogger(
    ILogger<AuthorizationFailureLogger> logger,
    TimeProvider clock) : IAuthorizationMiddlewareResultHandler
{
    private readonly AuthorizationMiddlewareResultHandler _default = new();

    public async Task HandleAsync(
        RequestDelegate next,
        HttpContext context,
        AuthorizationPolicy policy,
        PolicyAuthorizationResult authorizeResult)
    {
        ArgumentNullException.ThrowIfNull(context);
        ArgumentNullException.ThrowIfNull(authorizeResult);

        if (authorizeResult.Challenged || authorizeResult.Forbidden)
        {
            await RecordRefusalAsync(context, authorizeResult.Challenged);
        }

        await _default.HandleAsync(next, context, policy, authorizeResult);
    }

    private async Task RecordRefusalAsync(HttpContext context, bool challenged)
    {
        var services = context.RequestServices;
        var correlation = services.GetRequiredService<ICorrelationContext>();
        var currentUser = services.GetRequiredService<ICurrentUser>();
        var auditRecorder = services.GetRequiredService<IAuditRecorder>();

        var outcome = challenged ? "unauthenticated" : "forbidden";
        var operation = $"{context.Request.Method} {context.Request.Path}";

        logger.LogWarning(
            "Access refused ({Outcome}) for {Operation} (correlation {CorrelationId})",
            outcome,
            operation,
            correlation.Id);

        await auditRecorder.RecordAsync(new AuditEntry(
            Action: $"authorization.{outcome}",
            ActorId: currentUser.UserId,
            TargetType: "endpoint",
            TargetId: operation,
            OccurredAt: clock.GetUtcNow(),
            CorrelationId: correlation.Id,
            Metadata: new Dictionary<string, string>
            {
                ["population"] = currentUser.Population?.ToString() ?? "anonymous",
            }));
    }
}
