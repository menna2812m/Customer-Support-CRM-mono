using Crm.Api.Common.Correlation;
using Crm.Application.Common;
using Microsoft.AspNetCore.Mvc;

namespace Crm.Api.Common.Errors;

/// <summary>
/// The single producer of failure responses (spec FR-017, contracts/error-contract.md).
/// Controllers never write an error body of their own, which is what makes conformance testable.
/// </summary>
public static class ErrorContractSetup
{
    public static IServiceCollection AddCrmErrorContract(this IServiceCollection services)
    {
        ArgumentNullException.ThrowIfNull(services);

        services.AddProblemDetails(options =>
        {
            options.CustomizeProblemDetails = context =>
            {
                var problem = context.ProblemDetails;
                var status = problem.Status ?? context.HttpContext.Response.StatusCode;

                // A caller-visible code is mandatory. Derive it from the status unless a more
                // specific one was already supplied by the component raising the failure.
                if (!problem.Extensions.TryGetValue("code", out var existing) || existing is not string code)
                {
                    code = IsApiVersioningProblem(problem.Type)
                        ? ErrorCodes.UnsupportedApiVersion
                        : CodeForStatus(status);
                    problem.Extensions["code"] = code;
                }

                problem.Extensions["correlationId"] = context.HttpContext
                    .RequestServices.GetRequiredService<ICorrelationContext>().Id;

                problem.Instance ??= context.HttpContext.Request.Path.Value;
                problem.Type ??= ProblemTypes.ForCode(code);

                // Never leak the framework default detail for a server failure.
                if (status >= StatusCodes.Status500InternalServerError)
                {
                    problem.Title = GenericServerFailureTitle;
                    problem.Detail = null;
                }
            };
        });

        return services;
    }

    internal const string GenericServerFailureTitle =
        "The request could not be completed. Quote the correlation identifier when reporting this.";

    /// <summary>
    /// Asp.Versioning reports its failures as problem details with its own documentation type.
    /// Recognising it here keeps version errors on the shared contract without depending on a
    /// library extension point (spec FR-016).
    /// </summary>
    private static bool IsApiVersioningProblem(string? problemType) =>
        problemType?.Contains("api-versioning.org/problems", StringComparison.OrdinalIgnoreCase) == true;

    internal static string CodeForStatus(int status) => status switch
    {
        StatusCodes.Status400BadRequest => ErrorCodes.ValidationFailed,
        StatusCodes.Status401Unauthorized => ErrorCodes.Unauthenticated,
        StatusCodes.Status403Forbidden => ErrorCodes.Forbidden,
        StatusCodes.Status404NotFound => ErrorCodes.NotFound,
        StatusCodes.Status409Conflict => ErrorCodes.Conflict,
        _ => ErrorCodes.UnexpectedError,
    };

    /// <summary>
    /// Builds a problem response for a failure raised outside MVC (authentication challenges,
    /// unmatched routes, payload limits), so those paths return the same contract.
    /// </summary>
    public static async Task WriteProblemAsync(HttpContext context, int status, string code, string title)
    {
        ArgumentNullException.ThrowIfNull(context);

        context.Response.StatusCode = status;

        var problemDetails = new ProblemDetails
        {
            Status = status,
            Title = title,
            Extensions = { ["code"] = code },
        };

        var service = context.RequestServices.GetRequiredService<IProblemDetailsService>();
        await service.WriteAsync(new ProblemDetailsContext
        {
            HttpContext = context,
            ProblemDetails = problemDetails,
        });
    }
}
