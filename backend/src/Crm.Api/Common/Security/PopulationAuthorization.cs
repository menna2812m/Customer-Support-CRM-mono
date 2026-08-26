using Crm.Application.Abstractions;
using Microsoft.AspNetCore.Authorization;

namespace Crm.Api.Common.Security;

/// <summary>
/// Declares which caller populations may reach an operation (spec AR-004).
///
/// A permission name alone is not enough: an external portal account holding a permission of the
/// same name must still not reach a staff-only endpoint.
/// </summary>
[AttributeUsage(AttributeTargets.Class | AttributeTargets.Method, AllowMultiple = false)]
public sealed class RequirePopulationAttribute : AuthorizeAttribute
{
    public const string PolicyPrefix = "crm:pop:";

    public RequirePopulationAttribute(params CallerPopulation[] populations)
    {
        ArgumentNullException.ThrowIfNull(populations);

        if (populations.Length == 0)
        {
            throw new ArgumentException("At least one population must be admitted.", nameof(populations));
        }

        Populations = populations;
        Policy = PolicyPrefix + string.Join(',', populations.Select(p => p.ToString()));
    }

    public IReadOnlyList<CallerPopulation> Populations { get; }
}

public sealed class PopulationRequirement(IReadOnlyList<CallerPopulation> populations) : IAuthorizationRequirement
{
    public IReadOnlyList<CallerPopulation> Populations { get; } = populations;
}

public sealed class PopulationAuthorizationHandler(ICurrentUser currentUser)
    : AuthorizationHandler<PopulationRequirement>
{
    protected override Task HandleRequirementAsync(
        AuthorizationHandlerContext context,
        PopulationRequirement requirement)
    {
        ArgumentNullException.ThrowIfNull(context);
        ArgumentNullException.ThrowIfNull(requirement);

        if (currentUser.Population is { } population && requirement.Populations.Contains(population))
        {
            context.Succeed(requirement);
        }

        return Task.CompletedTask;
    }
}
