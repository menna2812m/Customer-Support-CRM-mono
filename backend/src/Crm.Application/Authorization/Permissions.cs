using System.Reflection;

namespace Crm.Application.Authorization;

/// <summary>
/// The permission catalog: code-declared, and the single source of truth for which permissions
/// exist (spec FR-024, clarification 2026-08-26).
///
/// Declaring these as constants means a misspelled permission is a build error rather than a
/// silently unenforced endpoint. No permission table is created by this feature; the future
/// users-and-permissions feature seeds role assignments from <see cref="All"/>.
///
/// Naming is <c>&lt;area&gt;.&lt;action&gt;</c>, lowercase, matching the constitution.
/// </summary>
public static class Permissions
{
    public static class Customers
    {
        public const string View = "customers.view";
        public const string Create = "customers.create";
        public const string Update = "customers.update";
    }

    public static class Tickets
    {
        public const string View = "tickets.view";
        public const string Create = "tickets.create";
        public const string Assign = "tickets.assign";
        public const string Escalate = "tickets.escalate";
    }

    public static class Users
    {
        public const string Manage = "users.manage";
    }

    public static class Reports
    {
        public const string View = "reports.view";
    }

    public static class Diagnostics
    {
        /// <summary>Used only by the removable reference slice.</summary>
        public const string Read = "diagnostics.read";
    }

    private static readonly Lazy<IReadOnlySet<string>> AllPermissions = new(Discover);

    /// <summary>Every declared permission. Enumerable at runtime so a later feature can seed from it.</summary>
    public static IReadOnlySet<string> All => AllPermissions.Value;

    public static bool Exists(string permission) => All.Contains(permission);

    private static HashSet<string> Discover()
    {
        var values = typeof(Permissions)
            .GetNestedTypes(BindingFlags.Public | BindingFlags.Static)
            .SelectMany(group => group.GetFields(BindingFlags.Public | BindingFlags.Static))
            .Where(field => field is { IsLiteral: true, IsInitOnly: false } && field.FieldType == typeof(string))
            .Select(field => (string)field.GetRawConstantValue()!)
            .ToList();

        var duplicates = values
            .GroupBy(value => value, StringComparer.Ordinal)
            .Where(group => group.Count() > 1)
            .Select(group => group.Key)
            .ToList();

        if (duplicates.Count > 0)
        {
            throw new InvalidOperationException(
                "The permission catalog contains duplicate values: " + string.Join(", ", duplicates));
        }

        return values.ToHashSet(StringComparer.Ordinal);
    }
}
