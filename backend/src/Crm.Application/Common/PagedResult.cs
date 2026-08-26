namespace Crm.Application.Common;

/// <summary>
/// The one paged response shape returned by every collection endpoint.
/// An empty page is a success with an empty <see cref="Items"/> list, never a 404.
/// </summary>
public sealed record PagedResult<T>
{
    public PagedResult(IReadOnlyList<T> items, int page, int pageSize, long totalCount)
    {
        ArgumentNullException.ThrowIfNull(items);

        Items = items;
        Page = page;
        PageSize = pageSize;
        TotalCount = totalCount;
    }

    /// <summary>Rows on this page. Never null.</summary>
    public IReadOnlyList<T> Items { get; }

    public int Page { get; }

    public int PageSize { get; }

    /// <summary>
    /// Total matching rows after filtering and after authorization scoping, before paging.
    /// </summary>
    public long TotalCount { get; }

    /// <summary>Derived. Zero when there are no matching rows.</summary>
    public int TotalPages => PageSize <= 0 ? 0 : (int)Math.Ceiling(TotalCount / (double)PageSize);
}
