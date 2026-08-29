using Asp.Versioning;
using Crm.Api.Common.Security;
using Crm.Api.Common.Validation;
using Crm.Application.Abstractions;
using Crm.Application.Authorization;
using Crm.Application.Common;
using Crm.Application.Organization;
using Crm.Domain.Organization;
using Crm.Infrastructure.Organization;
using Microsoft.AspNetCore.Mvc;

namespace Crm.Api.Organization;

/// <summary>
/// Branches: geography, and the simpler half of the model. A branch belongs to nothing and contains
/// nothing, so this controller is the department one without the containment rule.
/// </summary>
[ApiController]
[ApiVersion("1.0")]
[Route("api/v{version:apiVersion}/organization/branches")]
[RequirePermission(Permissions.Organization.View)]
[RequirePopulation(CallerPopulation.Staff)]
public sealed class BranchesController(OrganizationUnitService units) : ControllerBase
{
    [HttpGet]
    [RejectUnknownQuery("page", "pageSize", "sort", "search", "activeOnly")]
    [ProducesResponseType<PagedResult<OrganizationUnitRecord>>(StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status400BadRequest)]
    public async Task<ActionResult<PagedResult<OrganizationUnitRecord>>> List(
        [FromQuery] PageRequest paging,
        [FromQuery] string? search = null,
        [FromQuery] bool activeOnly = false,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(paging);

        var problem = ValidationProblems.From(
            HttpContext,
            PageRequestRules.ValidateSort(paging, OrganizationStore.SortableFields));

        if (problem is not null)
        {
            return BadRequest(problem);
        }

        var query = new UnitListQuery(paging, search, activeOnly, RequestLanguage.Of(HttpContext));

        return Ok(await units.ListAsync<Branch>(query, cancellationToken));
    }

    [HttpGet("{id:guid}")]
    [ProducesResponseType<OrganizationUnitRecord>(StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    public async Task<ActionResult<OrganizationUnitRecord>> Get(
        Guid id,
        CancellationToken cancellationToken = default)
    {
        var record = await units.FindAsync<Branch>(id, cancellationToken);

        return record is null
            ? OrganizationRefusal.NotFound.ToResult(HttpContext, null)
            : Ok(record);
    }

    [HttpPost]
    [RequirePermission(Permissions.Organization.Manage)]
    [ProducesResponseType<OrganizationUnitRecord>(StatusCodes.Status201Created)]
    [ProducesResponseType(StatusCodes.Status400BadRequest)]
    [ProducesResponseType(StatusCodes.Status409Conflict)]
    public async Task<ActionResult<OrganizationUnitRecord>> Create(
        [FromBody] CreateUnitRequest request,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(request);

        var outcome = await units.CreateAsync(
            Branch.Create,
            request.NameAr,
            request.NameEn,
            request.Code,
            cancellationToken);

        return outcome.Succeeded
            ? CreatedAtAction(nameof(Get), new { id = outcome.Value!.Id }, outcome.Value)
            : outcome.Refusal!.Value.ToResult(HttpContext, outcome.Detail);
    }

    [HttpPut("{id:guid}")]
    [RequirePermission(Permissions.Organization.Manage)]
    [ProducesResponseType<OrganizationUnitRecord>(StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status400BadRequest)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    [ProducesResponseType(StatusCodes.Status409Conflict)]
    public async Task<ActionResult<OrganizationUnitRecord>> Rename(
        Guid id,
        [FromBody] RenameUnitRequest request,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(request);

        var outcome = await units.RenameAsync<Branch>(
            id,
            request.NameAr,
            request.NameEn,
            cancellationToken);

        return outcome.Succeeded
            ? Ok(outcome.Value)
            : outcome.Refusal!.Value.ToResult(HttpContext, outcome.Detail);
    }

    [HttpPut("{id:guid}/activation")]
    [RequirePermission(Permissions.Organization.Manage)]
    [ProducesResponseType<OrganizationUnitRecord>(StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    public async Task<ActionResult<OrganizationUnitRecord>> SetActivation(
        Guid id,
        [FromBody] ActivationRequest request,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(request);

        var outcome = await units.SetActivationAsync<Branch>(id, request.IsActive, cancellationToken);

        return outcome.Succeeded
            ? Ok(outcome.Value)
            : outcome.Refusal!.Value.ToResult(HttpContext, outcome.Detail);
    }

    [HttpDelete("{id:guid}")]
    [RequirePermission(Permissions.Organization.Manage)]
    [ProducesResponseType(StatusCodes.Status204NoContent)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    [ProducesResponseType(StatusCodes.Status409Conflict)]
    public async Task<ActionResult> Delete(Guid id, CancellationToken cancellationToken = default)
    {
        var outcome = await units.DeleteAsync<Branch>(id, cancellationToken);

        return outcome.Succeeded
            ? NoContent()
            : outcome.Refusal!.Value.ToResult(HttpContext, outcome.Detail);
    }
}
