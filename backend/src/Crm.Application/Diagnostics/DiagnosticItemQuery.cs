using Crm.Application.Common;

namespace Crm.Application.Diagnostics;

/// <summary>Reference slice list item. No database table backs it (spec FR-051).</summary>
public sealed record DiagnosticItem(Guid Id, string Name, DateTimeOffset CreatedAt);

/// <summary>
/// Demonstrates the pagination contract end to end: allow-listed sorting, a rejected unknown sort
/// field, an honest total count, and a stable tiebreaker so paging cannot skip or duplicate rows.
/// </summary>
public sealed class DiagnosticItemQuery(TimeProvider clock)
{
    /// <summary>Sortable fields, per the contract: an endpoint publishes its allow-list.</summary>
    public static IReadOnlySet<string> SortableFields { get; } =
        new HashSet<string>(StringComparer.OrdinalIgnoreCase) { "name", "createdAt" };

    private const int TotalItems = 57;

    public PagedResult<DiagnosticItem> Execute(PageRequest request, string? nameContains = null)
    {
        ArgumentNullException.ThrowIfNull(request);

        var items = Generate().AsEnumerable();

        if (!string.IsNullOrWhiteSpace(nameContains))
        {
            items = items.Where(item =>
                item.Name.Contains(nameContains, StringComparison.OrdinalIgnoreCase));
        }

        var filtered = items.ToList();
        var sort = request.ParseSort();

        var ordered = sort switch
        {
            { Field: var field, Descending: var descending }
                when field.Equals("name", StringComparison.OrdinalIgnoreCase) =>
                descending
                    ? filtered.OrderByDescending(item => item.Name, StringComparer.Ordinal)
                    : filtered.OrderBy(item => item.Name, StringComparer.Ordinal),

            { Field: var field, Descending: var descending }
                when field.Equals("createdAt", StringComparison.OrdinalIgnoreCase) =>
                descending
                    ? filtered.OrderByDescending(item => item.CreatedAt)
                    : filtered.OrderBy(item => item.CreatedAt),

            _ => filtered.OrderBy(item => item.CreatedAt),
        };

        // Stable tiebreaker after the requested sort: without it, equal keys can shuffle between
        // page requests and a row is skipped or repeated.
        var page = ordered
            .ThenBy(item => item.Id)
            .Skip(request.Skip)
            .Take(request.PageSize)
            .ToList();

        return new PagedResult<DiagnosticItem>(page, request.Page, request.PageSize, filtered.Count);
    }

    private IEnumerable<DiagnosticItem> Generate()
    {
        var start = clock.GetUtcNow().AddDays(-TotalItems);

        for (var index = 0; index < TotalItems; index++)
        {
            yield return new DiagnosticItem(
                CreateStableId(index),
                $"Diagnostic item {index + 1:D2}",
                start.AddDays(index));
        }
    }

    private static Guid CreateStableId(int index)
    {
        Span<byte> bytes = stackalloc byte[16];
        BitConverter.TryWriteBytes(bytes, index);
        return new Guid(bytes);
    }
}
