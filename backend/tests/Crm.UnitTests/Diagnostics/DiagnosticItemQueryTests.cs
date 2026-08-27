using Crm.Application.Common;
using Crm.Application.Diagnostics;
using Shouldly;

namespace Crm.UnitTests.Diagnostics;

/// <summary>
/// Business-rule tests for the reference slice (spec FR-045, FR-047). The rules under test - page
/// arithmetic, stable ordering, honest totals - are the ones every future list endpoint inherits.
/// </summary>
public sealed class DiagnosticItemQueryTests
{
    private static readonly DateTimeOffset Now = new(2026, 8, 26, 12, 0, 0, TimeSpan.Zero);

    private readonly DiagnosticItemQuery _query = new(new FixedClock(Now));

    [Fact]
    public void The_first_page_starts_at_the_beginning()
    {
        var result = _query.Execute(new PageRequest { Page = 1, PageSize = 10 });

        result.Items.Count.ShouldBe(10);
        result.Page.ShouldBe(1);
        result.TotalCount.ShouldBe(57);
        result.TotalPages.ShouldBe(6);
    }

    [Fact]
    public void The_last_page_returns_only_the_remainder()
    {
        var result = _query.Execute(new PageRequest { Page = 6, PageSize = 10 });

        result.Items.Count.ShouldBe(7);
    }

    [Fact]
    public void A_page_past_the_end_is_empty_rather_than_an_error()
    {
        var result = _query.Execute(new PageRequest { Page = 99, PageSize = 10 });

        result.Items.ShouldBeEmpty();
        result.TotalCount.ShouldBe(57);
    }

    [Fact]
    public void Pages_do_not_overlap_or_skip_rows()
    {
        // The property that matters: walking every page must visit each row exactly once.
        var seen = new List<Guid>();

        for (var page = 1; page <= 6; page++)
        {
            seen.AddRange(_query.Execute(new PageRequest { Page = page, PageSize = 10 })
                .Items.Select(item => item.Id));
        }

        seen.Count.ShouldBe(57);
        seen.Distinct().Count().ShouldBe(57);
    }

    [Fact]
    public void Sorting_descending_reverses_the_order()
    {
        var ascending = _query.Execute(new PageRequest { PageSize = 5, Sort = "name" });
        var descending = _query.Execute(new PageRequest { PageSize = 5, Sort = "-name" });

        ascending.Items[0].Name.ShouldNotBe(descending.Items[0].Name);
        descending.Items.Select(item => item.Name)
            .ShouldBe(descending.Items.Select(item => item.Name).OrderDescending(StringComparer.Ordinal));
    }

    [Fact]
    public void Filtering_narrows_the_total_count_as_well_as_the_page()
    {
        var result = _query.Execute(new PageRequest(), nameContains: "item 01");

        result.Items.Count.ShouldBe(1);

        // A total that ignored the filter would make the pager lie about how many pages exist.
        result.TotalCount.ShouldBe(1);
    }

    [Fact]
    public void Total_pages_is_zero_when_nothing_matches()
    {
        var result = _query.Execute(new PageRequest(), nameContains: "no such item");

        result.Items.ShouldBeEmpty();
        result.TotalCount.ShouldBe(0);
        result.TotalPages.ShouldBe(0);
    }

    private sealed class FixedClock(DateTimeOffset now) : TimeProvider
    {
        public override DateTimeOffset GetUtcNow() => now;
    }
}
