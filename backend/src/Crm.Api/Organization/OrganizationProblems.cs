using Crm.Api.Common.Correlation;
using Crm.Application.Common;
using Crm.Application.Organization;
using Microsoft.AspNetCore.Mvc;

namespace Crm.Api.Organization;

/// <summary>
/// Turns an Application refusal into the shared error contract.
///
/// This lives in the API layer because the mapping from "why" to "HTTP status and code" is an HTTP
/// concern - the Application layer names the reason and stays free of the web framework
/// (Constitution I).
/// </summary>
internal static class OrganizationProblems
{
    /// <summary>
    /// Maps a refusal to a response. Every conflict carries a distinct code, so a client can show a
    /// specific message rather than a generic "conflict" the user cannot act on.
    /// </summary>
    internal static ActionResult ToResult(
        this OrganizationRefusal refusal,
        HttpContext context,
        string? detail)
    {
        ArgumentNullException.ThrowIfNull(context);

        return refusal switch
        {
            OrganizationRefusal.NotFound => new NotFoundObjectResult(
                Build(context, StatusCodes.Status404NotFound, ErrorCodes.NotFound, "Not found.", detail)),

            OrganizationRefusal.CodeConflict => new ConflictObjectResult(
                Build(
                    context,
                    StatusCodes.Status409Conflict,
                    ErrorCodes.OrganizationCodeConflict,
                    "That code is already in use.",
                    detail is null ? null : $"The code '{detail}' belongs to another unit.")),

            OrganizationRefusal.NameConflict => new ConflictObjectResult(
                Build(
                    context,
                    StatusCodes.Status409Conflict,
                    ErrorCodes.OrganizationNameConflict,
                    "That name is already in use.",
                    detail)),

            OrganizationRefusal.HasDependents => new ConflictObjectResult(
                Build(
                    context,
                    StatusCodes.Status409Conflict,
                    ErrorCodes.OrganizationHasDependents,
                    "This unit still has dependents.",
                    detail is null ? null : $"It still has {detail}.")),

            OrganizationRefusal.DepartmentInactive => new ConflictObjectResult(
                Build(
                    context,
                    StatusCodes.Status409Conflict,
                    ErrorCodes.OrganizationDepartmentInactive,
                    "The destination department is not active.",
                    detail)),

            _ => new ConflictObjectResult(
                Build(context, StatusCodes.Status409Conflict, ErrorCodes.Conflict, "Conflict.", detail)),
        };
    }

    private static ProblemDetails Build(
        HttpContext context,
        int status,
        string code,
        string title,
        string? detail) =>
        new()
        {
            Status = status,
            Title = title,
            Detail = detail,
            Type = ProblemTypes.ForCode(code),
            Instance = context.Request.Path.Value,
            Extensions =
            {
                ["code"] = code,
                ["correlationId"] = context.RequestServices
                    .GetRequiredService<ICorrelationContext>().Id,
            },
        };
}
