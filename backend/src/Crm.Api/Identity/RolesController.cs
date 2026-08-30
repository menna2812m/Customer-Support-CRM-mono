using Asp.Versioning;
using Crm.Api.Common.Security;
using Crm.Application.Abstractions;
using Crm.Application.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace Crm.Api.Identity;

/// <summary>
/// The roles a deployment seeds, readable so they can be granted.
/// </summary>
/// <remarks>
/// Read-only, and deliberately so. This feature grants authority; defining it - creating roles and
/// editing the permissions behind them - is a later feature with its own lockout risks. Each role
/// carries the permissions it grants so the interface can show a person's effective permissions as
/// derived from the roles above them rather than as something separately editable (FR-007).
/// </remarks>
[ApiController]
[ApiVersion("1.0")]
[Route("api/v{version:apiVersion}/identity/roles")]
[RequirePermission(Permissions.Identity.View)]
[RequirePopulation(CallerPopulation.Staff)]
public sealed class RolesController(IRoleCatalog roles) : ControllerBase
{
    [HttpGet]
    [ProducesResponseType<IReadOnlyList<RoleDetail>>(StatusCodes.Status200OK)]
    public async Task<ActionResult<IReadOnlyList<RoleDetail>>> List(
        CancellationToken cancellationToken = default) =>
        Ok(await roles.ListAsync(cancellationToken));
}
