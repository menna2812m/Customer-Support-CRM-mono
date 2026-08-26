using Crm.Application.Abstractions;
using Crm.Application.Authorization;
using Microsoft.AspNetCore.Authorization;

namespace Crm.Api.Common.Security;

/// <summary>
/// Declares the permission an operation requires (Constitution IV, spec FR-024).
///
/// Usage: <c>[RequirePermission(Permissions.Tickets.Assign)]</c>. The value must come from the
/// catalog, so a typo fails the build rather than leaving an endpoint unguarded.
/// </summary>
[AttributeUsage(AttributeTargets.Class | AttributeTargets.Method, AllowMultiple = true)]
public sealed class RequirePermissionAttribute : AuthorizeAttribute
{
    public const string PolicyPrefix = "crm:perm:";

    public RequirePermissionAttribute(string permission)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(permission);

        Permission = permission;
        Policy = PolicyPrefix + permission;
    }

    public string Permission { get; }
}

public sealed class PermissionRequirement(string permission) : IAuthorizationRequirement
{
    public string Permission { get; } = permission;
}

/// <summary>
/// Grants access only when the caller actually holds the declared permission. Authorization is
/// always enforced here, on the server: a frontend check is presentation only (Constitution IV).
/// </summary>
public sealed class PermissionAuthorizationHandler(ICurrentUser currentUser, ILogger<PermissionAuthorizationHandler> logger)
    : AuthorizationHandler<PermissionRequirement>
{
    protected override Task HandleRequirementAsync(
        AuthorizationHandlerContext context,
        PermissionRequirement requirement)
    {
        ArgumentNullException.ThrowIfNull(context);
        ArgumentNullException.ThrowIfNull(requirement);

        if (!Permissions.Exists(requirement.Permission))
        {
            // An endpoint referencing a permission outside the catalog is a programming error, and
            // failing open would be far worse than failing closed.
            logger.LogError(
                "Endpoint requires permission {Permission}, which is not in the catalog.",
                requirement.Permission);

            return Task.CompletedTask;
        }

        if (currentUser.IsAuthenticated && currentUser.Permissions.Contains(requirement.Permission))
        {
            context.Succeed(requirement);
        }

        return Task.CompletedTask;
    }
}
