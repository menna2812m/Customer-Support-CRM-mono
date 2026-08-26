namespace Crm.Application.Common;

/// <summary>
/// The one paging contract used by every collection endpoint
/// (specs/001-project-foundation/contracts/pagination-contract.md).
/// </summary>
public sealed class PageRequest
{
    public const int DefaultPageSize = 25;
    public const int MaxPageSize = 100;

    /// <summary>1-based page number.</summary>
    public int Page { get; set; } = 1;

    /// <summary>
    /// Items per page. Exceeding <see cref="MaxPageSize"/> is a validation failure rather than a
    /// silent clamp, so a caller never believes it received more than it did.
    /// </summary>
    public int PageSize { get; set; } = DefaultPageSize;

    /// <summary>
    /// Sort expression: <c>field</c> ascending, <c>-field</c> descending. The field must be on the
    /// endpoint's documented allow-list.
    /// </summary>
    public string? Sort { get; set; }

    /// <summary>Number of rows to skip for this page.</summary>
    public int Skip => (Page - 1) * PageSize;

    /// <summary>Parses <see cref="Sort"/> into a field name and direction.</summary>
    public (string Field, bool Descending)? ParseSort()
    {
        if (string.IsNullOrWhiteSpace(Sort))
        {
            return null;
        }

        var raw = Sort.Trim();
        var descending = raw.StartsWith('-');
        var field = descending ? raw[1..] : raw;

        return string.IsNullOrWhiteSpace(field) ? null : (field, descending);
    }
}
