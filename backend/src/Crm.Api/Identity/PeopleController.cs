using Asp.Versioning;
using Crm.Api.Common.Security;
using Crm.Api.Common.Validation;
using Crm.Api.Organization;
using Crm.Application.Abstractions;
using Crm.Application.Authorization;
using Crm.Application.Common;
using Crm.Application.Identity.People;
using Microsoft.AspNetCore.Mvc;

namespace Crm.Api.Identity;

/// <summary>
/// People: who exists, what they may do, and where they sit (feature 004).
/// </summary>
/// <remarks>
/// Administration is global rather than scoped by the caller's own placement (AR-004). Scoping the
/// people list by the placement this feature exists to assign would be circular.
/// </remarks>
[ApiController]
[ApiVersion("1.0")]
[Route("api/v{version:apiVersion}/identity/people")]
[RequirePermission(Permissions.Identity.View)]
[RequirePopulation(CallerPopulation.Staff)]
public sealed class PeopleController(PeopleService people) : ControllerBase
{
    [HttpGet]
    [RejectUnknownQuery("page", "pageSize", "search", "branchId", "departmentId", "teamId", "activeOnly", "unlinkedOnly")]
    [ProducesResponseType<PagedResult<PersonSummary>>(StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status400BadRequest)]
    public async Task<ActionResult<PagedResult<PersonSummary>>> List(
        [FromQuery] PageRequest paging,
        [FromQuery] string? search = null,
        [FromQuery] Guid? branchId = null,
        [FromQuery] Guid? departmentId = null,
        [FromQuery] Guid? teamId = null,
        [FromQuery] bool activeOnly = false,
        [FromQuery] bool unlinkedOnly = false,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(paging);

        var query = new PeopleQuery(
            paging,
            search,
            branchId,
            departmentId,
            teamId,
            activeOnly,
            unlinkedOnly,
            RequestLanguage.Of(HttpContext));

        return Ok(await people.ListAsync(query, cancellationToken));
    }

    [HttpGet("{id:guid}")]
    [ProducesResponseType<PersonDetail>(StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    public async Task<ActionResult<PersonDetail>> Get(Guid id, CancellationToken cancellationToken = default)
    {
        var person = await people.FindAsync(id, cancellationToken);

        return person is null
            ? PersonRefusal.NotFound.ToResult(HttpContext)
            : Ok(person);
    }

    [HttpPost]
    [RequirePermission(Permissions.Identity.Manage)]
    [ProducesResponseType<PersonDetail>(StatusCodes.Status201Created)]
    [ProducesResponseType(StatusCodes.Status400BadRequest)]
    [ProducesResponseType(StatusCodes.Status409Conflict)]
    public async Task<ActionResult<PersonDetail>> PreProvision(
        [FromBody] PreProvisionRequest request,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(request);

        var result = await people.PreProvisionAsync(
            request.Email,
            request.DisplayName,
            request.RoleIds ?? [],
            request.Placement,
            cancellationToken);

        return result.IsSuccess
            ? CreatedAtAction(nameof(Get), new { id = result.Person!.Summary.Id }, result.Person)
            : result.Refusal.ToResult(HttpContext);
    }

    [HttpPut("{id:guid}/placement")]
    [RequirePermission(Permissions.Identity.Manage)]
    [ProducesResponseType<PersonDetail>(StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    [ProducesResponseType(StatusCodes.Status409Conflict)]
    public async Task<ActionResult<PersonDetail>> SetPlacement(
        Guid id,
        [FromBody] PlacementRequest request,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(request);

        var result = await people.SetPlacementAsync(
            id,
            new PlacementCommand(request.BranchId, request.DepartmentId, request.TeamId),
            cancellationToken);

        return result.IsSuccess ? Ok(result.Person) : result.Refusal.ToResult(HttpContext);
    }

    [HttpPut("{id:guid}/activation")]
    [RequirePermission(Permissions.Identity.Manage)]
    [ProducesResponseType<PersonDetail>(StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    [ProducesResponseType(StatusCodes.Status409Conflict)]
    public async Task<ActionResult<PersonDetail>> SetActivation(
        Guid id,
        [FromBody] ActivationRequest request,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(request);

        var result = await people.SetActivationAsync(id, request.IsActive, cancellationToken);

        return result.IsSuccess ? Ok(result.Person) : result.Refusal.ToResult(HttpContext);
    }

    [HttpPost("{id:guid}/roles/{roleId:guid}")]
    [RequirePermission(Permissions.Identity.Manage)]
    [ProducesResponseType<PersonDetail>(StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    public async Task<ActionResult<PersonDetail>> GrantRole(
        Guid id,
        Guid roleId,
        CancellationToken cancellationToken = default)
    {
        var result = await people.GrantRoleAsync(id, roleId, cancellationToken);

        return result.IsSuccess ? Ok(result.Person) : result.Refusal.ToResult(HttpContext);
    }

    [HttpDelete("{id:guid}/roles/{roleId:guid}")]
    [RequirePermission(Permissions.Identity.Manage)]
    [ProducesResponseType<PersonDetail>(StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    [ProducesResponseType(StatusCodes.Status409Conflict)]
    public async Task<ActionResult<PersonDetail>> RevokeRole(
        Guid id,
        Guid roleId,
        CancellationToken cancellationToken = default)
    {
        var result = await people.RevokeRoleAsync(id, roleId, cancellationToken);

        return result.IsSuccess ? Ok(result.Person) : result.Refusal.ToResult(HttpContext);
    }

    [HttpDelete("{id:guid}")]
    [RequirePermission(Permissions.Identity.Manage)]
    [ProducesResponseType(StatusCodes.Status204NoContent)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    [ProducesResponseType(StatusCodes.Status409Conflict)]
    public async Task<ActionResult> Delete(Guid id, CancellationToken cancellationToken = default)
    {
        var result = await people.DeleteAsync(id, cancellationToken);

        return result.Deleted ? NoContent() : result.Refusal.ToResult(HttpContext);
    }
}

/// <summary>Preparing somebody by email address before their first sign-in.</summary>
public sealed record PreProvisionRequest
{
    public string Email { get; init; } = string.Empty;

    /// <summary>
    /// A working label until the provider supplies the real one, which overwrites it on the first
    /// sign-in - the provider owns a person's name (spec FR-004).
    /// </summary>
    public string DisplayName { get; init; } = string.Empty;

    public IReadOnlyList<Guid>? RoleIds { get; init; }

    public PlacementCommand? Placement { get; init; }
}

/// <summary>All three may be null, which clears the placement.</summary>
public sealed record PlacementRequest
{
    public Guid? BranchId { get; init; }

    public Guid? DepartmentId { get; init; }

    public Guid? TeamId { get; init; }
}

public sealed record ActivationRequest
{
    public bool IsActive { get; init; }
}
