using Crm.Application.Abstractions;
using Crm.Application.Authorization;

namespace Crm.Api.Configuration;

/// <summary>
/// Checks the role-to-permission store against the code-declared catalog at startup (spec FR-024).
///
/// The failure this exists to catch is silent: a permission is renamed or removed in code, the
/// seeded row keeps the old name, and every role that relied on it quietly grants less than it
/// says. Nobody notices until somebody who should be able to do their job cannot, and the reason
/// is invisible from both the role screen and the endpoint.
///
/// Reported at startup, where a deployment can still be rolled back, rather than discovered later
/// as a support ticket.
/// </summary>
public static class SeededPermissionValidation
{
    /// <summary>
    /// Refuses to start when a stored role grants a permission the catalog does not declare.
    ///
    /// A database that cannot be reached is a different problem with its own reporting - readiness
    /// already says so - and is deliberately not turned into a startup crash here, because that
    /// would make an unreachable database indistinguishable from a misconfigured one.
    /// </summary>
    public static async Task ValidateSeededPermissionsAsync(this IServiceProvider services)
    {
        ArgumentNullException.ThrowIfNull(services);

        using var scope = services.CreateScope();

        var logger = scope.ServiceProvider
            .GetRequiredService<ILoggerFactory>()
            .CreateLogger(typeof(SeededPermissionValidation).FullName!);

        IReadOnlyList<RolePermissionGrant> grants;

        try
        {
            grants = await scope.ServiceProvider
                .GetRequiredService<IIdentityStore>()
                .GetRolePermissionsAsync();
        }
        catch (Exception exception) when (exception is not OperationCanceledException)
        {
            logger.LogError(
                exception,
                "Seeded role permissions could not be read, so they were not checked against the "
                    + "permission catalog. Readiness will report the database as unhealthy.");

            return;
        }

        var unknown = grants
            .Where(grant => !Permissions.Exists(grant.Permission))
            .OrderBy(grant => grant.RoleName, StringComparer.Ordinal)
            .ThenBy(grant => grant.Permission, StringComparer.Ordinal)
            .ToList();

        if (unknown.Count == 0)
        {
            if (logger.IsEnabled(LogLevel.Information))
            {
                logger.LogInformation(
                    "Checked {GrantCount} stored role permission(s) against the catalog; all are declared.",
                    grants.Count);
            }

            return;
        }

        throw new InvalidOperationException(
            $"The application cannot start because {unknown.Count} stored role permission(s) are not "
                + "declared in the permission catalog. Either restore the permission in "
                + $"{nameof(Permissions)} or remove the grant:"
                + Environment.NewLine
                + string.Join(
                    Environment.NewLine,
                    unknown.Select(grant => $"  - role '{grant.RoleName}' grants '{grant.Permission}'")));
    }
}
