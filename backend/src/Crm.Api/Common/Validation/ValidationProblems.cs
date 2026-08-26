using Crm.Api.Common.Correlation;
using Crm.Application.Common;
using Microsoft.AspNetCore.Mvc;

namespace Crm.Api.Common.Validation;

/// <summary>
/// Turns Application-layer field failures into the shared error contract. The API layer owns the
/// HTTP shape; the Application layer owns the rules.
///
/// Everything goes through here so that <c>code</c> and <c>correlationId</c> are always present:
/// a result written directly as an <see cref="ObjectResult"/> bypasses the central problem-details
/// customizer, and a failure without a correlation identifier cannot be traced to its logs.
/// </summary>
public static class ValidationProblems
{
    public static ProblemDetails? From(
        HttpContext context,
        IReadOnlyList<FieldFailure> failures,
        string code = ErrorCodes.ValidationFailed,
        string title = "One or more validation errors occurred.")
    {
        ArgumentNullException.ThrowIfNull(context);
        ArgumentNullException.ThrowIfNull(failures);

        return failures.Count == 0 ? null : Build(context, failures, code, title);
    }

    /// <summary>Builds the problem for failures that are already known to exist.</summary>
    public static ProblemDetails Build(
        HttpContext context,
        IReadOnlyList<FieldFailure> failures,
        string code = ErrorCodes.ValidationFailed,
        string title = "One or more validation errors occurred.")
    {
        ArgumentNullException.ThrowIfNull(context);
        ArgumentNullException.ThrowIfNull(failures);

        return new ProblemDetails
        {
            Status = StatusCodes.Status400BadRequest,
            Title = title,
            Type = ProblemTypes.ForCode(code),
            Instance = context.Request.Path.Value,
            Extensions =
            {
                ["code"] = code,
                ["correlationId"] = context.RequestServices.GetRequiredService<ICorrelationContext>().Id,
                ["errors"] = failures
                    .Select(failure => new
                    {
                        field = failure.Field,
                        code = failure.Code,
                        message = failure.Message,
                    })
                    .ToArray(),
            },
        };
    }
}
