using FluentValidation;

namespace Crm.Application.Common;

/// <summary>A rejected field, in the shape the error contract publishes.</summary>
public sealed record FieldFailure(string Field, string Code, string Message);

/// <summary>
/// Paging rules that every list endpoint shares. Kept free of HTTP types so the Application layer
/// stays independent of the web framework (Constitution I) - the API layer turns these failures
/// into the error contract.
/// </summary>
public static class PageRequestRules
{
    /// <summary>
    /// Checks the sort expression against the endpoint's allow-list. Page and page size are
    /// checked by <see cref="PageRequestValidator"/>, which the validation filter applies
    /// automatically.
    /// </summary>
    public static IReadOnlyList<FieldFailure> ValidateSort(
        PageRequest request,
        IReadOnlySet<string> sortableFields)
    {
        ArgumentNullException.ThrowIfNull(request);
        ArgumentNullException.ThrowIfNull(sortableFields);

        var sort = request.ParseSort();

        if (sort is null)
        {
            return [];
        }

        if (sortableFields.Contains(sort.Value.Field))
        {
            return [];
        }

        // Rejecting rather than ignoring: silently sorting by something else is a bug report
        // waiting to happen, and an open sort surface is a column-probing surface.
        return
        [
            new FieldFailure(
                "sort",
                ErrorCodes.Field.NotSortable,
                $"Cannot sort by '{sort.Value.Field}'. Sortable fields: "
                    + string.Join(", ", sortableFields.Order(StringComparer.Ordinal)) + "."),
        ];
    }
}

/// <summary>
/// Applied to every endpoint that accepts a <see cref="PageRequest"/>, because the validation
/// filter resolves validators by argument type.
/// </summary>
public sealed class PageRequestValidator : AbstractValidator<PageRequest>
{
    public PageRequestValidator()
    {
        RuleFor(request => request.Page)
            .GreaterThanOrEqualTo(1)
            .WithErrorCode(ErrorCodes.Field.Range)
            .WithMessage("Page must be 1 or greater.");

        RuleFor(request => request.PageSize)
            .InclusiveBetween(1, PageRequest.MaxPageSize)
            .WithErrorCode(ErrorCodes.Field.Range)
            .WithMessage($"Page size must be between 1 and {PageRequest.MaxPageSize}.");
    }
}
