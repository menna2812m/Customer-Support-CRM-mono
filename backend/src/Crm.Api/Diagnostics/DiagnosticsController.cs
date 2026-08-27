using Asp.Versioning;
using Crm.Api.Common.Correlation;
using Crm.Api.Common.Security;
using Crm.Api.Common.Validation;
using Crm.Application.Abstractions;
using Crm.Application.Authorization;
using Crm.Application.Common;
using Crm.Application.Diagnostics;
using Microsoft.AspNetCore.Mvc;

namespace Crm.Api.Diagnostics;

/// <summary>
/// The reference vertical slice (spec FR-051).
///
/// It carries no business data and exists to prove the conventions a real feature will inherit:
/// versioned routing, declared permission and admitted populations, validation before any logic,
/// the shared error contract, and the pagination contract. Deleting this file, its Application
/// counterparts, and the matching Angular feature removes the slice completely.
///
/// Note how thin it is - Constitution I: controllers do HTTP, not business rules.
/// </summary>
[ApiController]
[ApiVersion("1.0")]
[Route("api/v{version:apiVersion}/diagnostics")]
[RequirePermission(Permissions.Diagnostics.Read)]
[RequirePopulation(CallerPopulation.Staff)]
public sealed class DiagnosticsController(
    DiagnosticItemQuery query,
    ICorrelationContext correlation,
    TimeProvider clock) : ControllerBase
{
    /// <summary>Echoes a validated payload. Stores nothing.</summary>
    [HttpPost("echo")]
    [ProducesResponseType<EchoResponse>(StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status400BadRequest)]
    [ProducesResponseType(StatusCodes.Status401Unauthorized)]
    [ProducesResponseType(StatusCodes.Status403Forbidden)]
    public ActionResult<EchoResponse> Echo([FromBody] EchoRequest request)
    {
        ArgumentNullException.ThrowIfNull(request);

        var message = string.Join(
            ' ',
            Enumerable.Repeat(request.Message, request.RepeatCount));

        return Ok(new EchoResponse(message, clock.GetUtcNow(), correlation.Id));
    }

    /// <summary>A page of generated diagnostic items, following the shared pagination contract.</summary>
    [HttpGet("items")]
    [RejectUnknownQuery("page", "pageSize", "sort", "nameContains")]
    [ProducesResponseType<PagedResult<DiagnosticItem>>(StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status400BadRequest)]
    public ActionResult<PagedResult<DiagnosticItem>> GetItems(
        [FromQuery] PageRequest paging,
        [FromQuery] string? nameContains = null)
    {
        ArgumentNullException.ThrowIfNull(paging);

        var problem = ValidationProblems.From(
            HttpContext,
            PageRequestRules.ValidateSort(paging, DiagnosticItemQuery.SortableFields));

        if (problem is not null)
        {
            return BadRequest(problem);
        }

        return Ok(query.Execute(paging, nameContains));
    }

    /// <summary>Deliberate failure used to verify that nothing internal leaks (spec FR-018).</summary>
    [HttpGet("boom")]
    [ProducesResponseType(StatusCodes.Status500InternalServerError)]
    public ActionResult Boom() =>
        throw new InvalidOperationException(
            "Deliberate failure for contract verification. Connection string Server=secret;Password=hunter2");
}
