namespace Crm.Application.Abstractions;

/// <summary>
/// The roles a deployment seeds, and the permissions each one grants.
/// </summary>
/// <remarks>
/// Read-only by design. Feature 004 grants roles; the feature that defines them edits this data,
/// and giving this interface a write method now would invite somebody to reach for it before those
/// rules exist.
/// </remarks>
public interface IRoleCatalog
{
    Task<IReadOnlyList<RoleDetail>> ListAsync(CancellationToken cancellationToken = default);
}

/// <summary>A role together with what it grants, so effective permissions can be shown as derived.</summary>
public sealed record RoleDetail(Guid Id, string Name, IReadOnlyList<string> Permissions);
