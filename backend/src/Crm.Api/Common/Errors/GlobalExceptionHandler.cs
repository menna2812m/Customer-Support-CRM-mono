using Crm.Api.Common.Correlation;
using Crm.Application.Common;
using Microsoft.AspNetCore.Diagnostics;
using Microsoft.AspNetCore.Mvc;

namespace Crm.Api.Common.Errors;

/// <summary>
/// Converts anything that escapes a request handler into the shared error contract.
///
/// Spec FR-018: the caller receives a generic message and the correlation identifier - never a
/// stack trace, exception type, SQL, or configuration value. The detail goes to the log, which is
/// why the correlation identifier is the only handle handed out.
/// </summary>
public sealed class GlobalExceptionHandler(
    ILogger<GlobalExceptionHandler> logger,
    IProblemDetailsService problemDetailsService) : IExceptionHandler
{
    public async ValueTask<bool> TryHandleAsync(
        HttpContext httpContext,
        Exception exception,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(httpContext);

        // The handler itself is a singleton, so the per-request correlation context is resolved
        // from the request scope rather than injected.
        var correlation = httpContext.RequestServices.GetRequiredService<ICorrelationContext>();

        var (status, code) = Classify(exception);

        if (status >= StatusCodes.Status500InternalServerError)
        {
            logger.LogError(
                exception,
                "Unhandled exception for {Method} {Path} (correlation {CorrelationId})",
                httpContext.Request.Method,
                httpContext.Request.Path,
                correlation.Id);
        }
        else
        {
            logger.LogWarning(
                "Request rejected with {Status} {Code} for {Method} {Path} (correlation {CorrelationId})",
                status,
                code,
                httpContext.Request.Method,
                httpContext.Request.Path,
                correlation.Id);
        }

        httpContext.Response.StatusCode = status;

        return await problemDetailsService.TryWriteAsync(new ProblemDetailsContext
        {
            HttpContext = httpContext,
            ProblemDetails = new ProblemDetails
            {
                Status = status,
                Title = TitleFor(status, code),
                Extensions = { ["code"] = code },
            },
        });
    }

    /// <summary>Client closed the connection before the response was written.</summary>
    private const int ClientClosedRequest = 499;

    private static (int Status, string Code) Classify(Exception exception) => exception switch
    {
        BadHttpRequestException => (StatusCodes.Status400BadRequest, ErrorCodes.MalformedRequest),
        System.Text.Json.JsonException => (StatusCodes.Status400BadRequest, ErrorCodes.MalformedRequest),
        OperationCanceledException => (ClientClosedRequest, ErrorCodes.MalformedRequest),
        _ => (StatusCodes.Status500InternalServerError, ErrorCodes.UnexpectedError),
    };

    private static string TitleFor(int status, string code) =>
        status >= StatusCodes.Status500InternalServerError
            ? ErrorContractSetup.GenericServerFailureTitle
            : code switch
            {
                ErrorCodes.MalformedRequest => "The request could not be read.",
                _ => "The request could not be completed.",
            };
}
