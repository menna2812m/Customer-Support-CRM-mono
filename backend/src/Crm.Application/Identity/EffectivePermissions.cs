using Crm.Application.Authorization;

namespace Crm.Application.Identity;

/// <summary>
/// What a user may actually do: the union of every permission their roles grant (spec FR-021).
///
/// A rule rather than a query, which is why it lives here and not in the store. Two properties
/// matter. Roles add up rather than override each other, so somebody who is both an agent and a
/// supervisor can do both jobs - the alternative, letting one role win, would make the outcome
/// depend on an ordering nobody chose. And a granted name that the catalog does not declare grants
/// nothing, because a permission no endpoint checks cannot open a door: keeping it in the set would
/// only make the session look more capable than it is.
///
/// Dropping an unknown name is deliberately not silent. Startup reports it against the catalog, and
/// the caller here is given the list so it can be logged where somebody will see it.
/// </summary>
public static class EffectivePermissions
{
    /// <summary>
    /// Collapses grants into the set the credential will carry, separating out any name the
    /// catalog does not declare.
    /// </summary>
    public static EffectivePermissionSet Resolve(IEnumerable<string> granted)
    {
        ArgumentNullException.ThrowIfNull(granted);

        var permissions = new HashSet<string>(StringComparer.Ordinal);
        var unknown = new HashSet<string>(StringComparer.Ordinal);

        foreach (var permission in granted)
        {
            if (string.IsNullOrWhiteSpace(permission))
            {
                continue;
            }

            if (Permissions.Exists(permission))
            {
                permissions.Add(permission);
            }
            else
            {
                unknown.Add(permission);
            }
        }

        return new EffectivePermissionSet(permissions, unknown);
    }
}

/// <param name="Permissions">What the session carries. Every name here exists in the catalog.</param>
/// <param name="Unknown">Names the catalog does not declare. Granted nothing; worth reporting.</param>
public sealed record EffectivePermissionSet(
    IReadOnlySet<string> Permissions,
    IReadOnlySet<string> Unknown);
