using Crm.Application.Common;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.Filters;

namespace Crm.Api.Common.Validation;

/// <summary>
/// Rejects query parameters the endpoint does not publish (pagination contract).
///
/// Ignoring an unknown parameter is worse than refusing it: a caller that misspells a filter gets
/// the unfiltered collection back and believes it filtered.
/// </summary>
[AttributeUsage(AttributeTargets.Method, AllowMultiple = false)]
public sealed class RejectUnknownQueryAttribute(params string[] allowed) : ActionFilterAttribute
{
    // Supplied by the versioning stack rather than by the endpoint author.
    private static readonly string[] FrameworkParameters = ["api-version", "apiVersion"];

    private readonly HashSet<string> _allowed =
        new(allowed.Concat(FrameworkParameters), StringComparer.OrdinalIgnoreCase);

    public override void OnActionExecuting(ActionExecutingContext context)
    {
        ArgumentNullException.ThrowIfNull(context);

        var unknown = context.HttpContext.Request.Query.Keys
            .Where(key => !_allowed.Contains(key))
            .ToList();

        if (unknown.Count == 0)
        {
            base.OnActionExecuting(context);
            return;
        }

        var failures = unknown
            .Select(key => new FieldFailure(
                key,
                ErrorCodes.Field.UnknownParameter,
                $"'{key}' is not a supported query parameter for this endpoint."))
            .ToList();

        context.Result = new BadRequestObjectResult(
            // The top-level code stays validation_failed per the contract; the per-field code says
            // what was wrong with each parameter.
            ValidationProblems.Build(context.HttpContext, failures))
        {
            ContentTypes = { "application/problem+json" },
        };
    }
}
