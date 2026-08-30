using Crm.Api.Common.Correlation;
using Crm.Application.Abstractions;
using Crm.Application.Common;
using Microsoft.AspNetCore.Mvc;

namespace Crm.Api.Identity;

/// <summary>
/// Turns a people refusal into the shared error contract.
///
/// This lives in the API layer because the mapping from "why" to "HTTP status and code" is an HTTP
/// concern - the Application layer names the reason and stays free of the web framework
/// (Constitution I).
/// </summary>
internal static class IdentityProblems
{
    /// <summary>
    /// Maps a refusal to a response. Every conflict carries a distinct code, so the client can show
    /// a sentence the reader can act on rather than a generic "conflict" that explains nothing.
    /// </summary>
    /// <remarks>
    /// The titles here are developer-facing, as the error contract says. The client never displays
    /// them: it switches on the code and supplies its own wording in the reader's language (LR-004).
    /// </remarks>
    internal static ActionResult ToResult(this PersonRefusal refusal, HttpContext context)
    {
        ArgumentNullException.ThrowIfNull(context);

        return refusal switch
        {
            PersonRefusal.NotFound => new NotFoundObjectResult(
                Build(context, StatusCodes.Status404NotFound, ErrorCodes.NotFound, "Not found.")),

            PersonRefusal.EmailInUse => new ConflictObjectResult(
                Build(
                    context,
                    StatusCodes.Status409Conflict,
                    ErrorCodes.IdentityEmailInUse,
                    "That email address already belongs to someone.")),

            PersonRefusal.LastAdministrator => new ConflictObjectResult(
                Build(
                    context,
                    StatusCodes.Status409Conflict,
                    ErrorCodes.IdentityLastAdministrator,
                    "The system must keep at least one active administrator.")),

            PersonRefusal.SelfDemotion => new ConflictObjectResult(
                Build(
                    context,
                    StatusCodes.Status409Conflict,
                    ErrorCodes.IdentitySelfDemotion,
                    "An administrator cannot make this change to their own account.")),

            PersonRefusal.PlacementMismatch => new ConflictObjectResult(
                Build(
                    context,
                    StatusCodes.Status409Conflict,
                    ErrorCodes.IdentityPlacementMismatch,
                    "The named department does not match the team's department.")),

            PersonRefusal.UnitInactive => new ConflictObjectResult(
                Build(
                    context,
                    StatusCodes.Status409Conflict,
                    ErrorCodes.OrganizationDepartmentInactive,
                    "A chosen unit is not active.")),

            _ => new ConflictObjectResult(
                Build(context, StatusCodes.Status409Conflict, ErrorCodes.Conflict, "Conflict.")),
        };
    }

    private static ProblemDetails Build(HttpContext context, int status, string code, string title) =>
        new()
        {
            Status = status,
            Title = title,
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
