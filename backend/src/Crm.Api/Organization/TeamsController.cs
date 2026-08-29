using Asp.Versioning;
using Crm.Api.Common.Security;
using Crm.Application.Abstractions;
using Crm.Application.Authorization;
using Crm.Application.Organization;
using Microsoft.AspNetCore.Mvc;

namespace Crm.Api.Organization;

/// <summary>
/// A team addressed on its own, rather than under its department.
///
/// Creating and listing teams belongs to <see cref="DepartmentsController"/>, because those are the
/// operations where the containment rule should be visible. Everything here addresses a team that
/// already exists and already knows which department it is in - including the move, which is the
/// one operation that necessarily reaches a team from outside its current department.
/// </summary>
[ApiController]
[ApiVersion("1.0")]
[Route("api/v{version:apiVersion}/organization/teams")]
[RequirePermission(Permissions.Organization.View)]
[RequirePopulation(CallerPopulation.Staff)]
public sealed class TeamsController(TeamService teams) : ControllerBase
{
    [HttpGet("{id:guid}")]
    [ProducesResponseType<TeamRecord>(StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    public async Task<ActionResult<TeamRecord>> Get(
        Guid id,
        CancellationToken cancellationToken = default)
    {
        var record = await teams.FindAsync(id, cancellationToken);

        return record is null
            ? OrganizationRefusal.NotFound.ToResult(HttpContext, null)
            : Ok(record);
    }

    [HttpPut("{id:guid}")]
    [RequirePermission(Permissions.Organization.Manage)]
    [ProducesResponseType<TeamRecord>(StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status400BadRequest)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    [ProducesResponseType(StatusCodes.Status409Conflict)]
    public async Task<ActionResult<TeamRecord>> Rename(
        Guid id,
        [FromBody] RenameUnitRequest request,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(request);

        var outcome = await teams.RenameAsync(id, request.NameAr, request.NameEn, cancellationToken);

        return outcome.Succeeded
            ? Ok(outcome.Value)
            : outcome.Refusal!.Value.ToResult(HttpContext, outcome.Detail);
    }

    [HttpPut("{id:guid}/activation")]
    [RequirePermission(Permissions.Organization.Manage)]
    [ProducesResponseType<TeamRecord>(StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    public async Task<ActionResult<TeamRecord>> SetActivation(
        Guid id,
        [FromBody] ActivationRequest request,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(request);

        var outcome = await teams.SetActivationAsync(id, request.IsActive, cancellationToken);

        return outcome.Succeeded
            ? Ok(outcome.Value)
            : outcome.Refusal!.Value.ToResult(HttpContext, outcome.Detail);
    }

    /// <summary>
    /// Moves the team to another department, carrying its members with it (spec FR-015). The
    /// response reports how many people were affected, because that is the part an administrator
    /// cannot see for themselves.
    /// </summary>
    [HttpPut("{id:guid}/department")]
    [RequirePermission(Permissions.Organization.Manage)]
    [ProducesResponseType<TeamMoveResult>(StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status400BadRequest)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    [ProducesResponseType(StatusCodes.Status409Conflict)]
    public async Task<ActionResult<TeamMoveResult>> Move(
        Guid id,
        [FromBody] MoveTeamRequest request,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(request);

        var outcome = await teams.MoveAsync(id, request.DepartmentId, cancellationToken);

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
        var outcome = await teams.DeleteAsync(id, cancellationToken);

        return outcome.Succeeded
            ? NoContent()
            : outcome.Refusal!.Value.ToResult(HttpContext, outcome.Detail);
    }
}
