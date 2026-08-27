using FluentValidation;

namespace Crm.Application.Common;

/// <summary>
/// Shared validation rules every feature reuses, so a limit is defined once rather than guessed
/// per endpoint (spec FR-055).
/// </summary>
public static class ValidationRules
{
    /// <summary>Default maximum number of items in any request collection.</summary>
    public const int MaxCollectionItems = 500;

    /// <summary>
    /// Caps a collection at <see cref="MaxCollectionItems"/> unless the endpoint lowers it.
    /// Raising the limit is a reviewed exception, not a local decision.
    /// </summary>
    public static IRuleBuilderOptions<T, IReadOnlyList<TItem>> MaxItems<T, TItem>(
        this IRuleBuilder<T, IReadOnlyList<TItem>> rule,
        int maximum = MaxCollectionItems)
    {
        ArgumentNullException.ThrowIfNull(rule);

        return rule
            .Must(collection => (collection?.Count ?? 0) <= maximum)
            .WithErrorCode(ErrorCodes.Field.TooManyItems)
            .WithMessage($"At most {maximum} items are allowed.");
    }
}
