using Crm.Application.Common;
using FluentValidation;

namespace Crm.Application.Organization;

/// <summary>Creating a unit. The code appears here and nowhere else, because it never changes.</summary>
public sealed class CreateUnitRequest
{
    public string NameAr { get; set; } = string.Empty;

    public string NameEn { get; set; } = string.Empty;

    public string Code { get; set; } = string.Empty;
}

/// <summary>
/// Renaming a unit. Both names are required together, so a unit cannot be created in one language
/// and completed in the other later (spec LR-003). The code is deliberately absent rather than
/// present and ignored - a field a caller can send and have silently discarded is worse than one
/// that does not exist.
/// </summary>
public sealed class RenameUnitRequest
{
    public string NameAr { get; set; } = string.Empty;

    public string NameEn { get; set; } = string.Empty;
}

public sealed class ActivationRequest
{
    public bool IsActive { get; set; }
}

public sealed class MoveTeamRequest
{
    public Guid DepartmentId { get; set; }
}

/// <summary>Shared limits, so the form and the server cannot disagree about them.</summary>
public static class UnitLimits
{
    public const int MaxNameLength = 200;
    public const int MaxCodeLength = 32;
}

public sealed class CreateUnitRequestValidator : AbstractValidator<CreateUnitRequest>
{
    public CreateUnitRequestValidator()
    {
        RuleFor(request => request.NameAr)
            .NotEmpty().WithErrorCode(ErrorCodes.Field.Required)
            .MaximumLength(UnitLimits.MaxNameLength).WithErrorCode(ErrorCodes.Field.MaxLength);

        RuleFor(request => request.NameEn)
            .NotEmpty().WithErrorCode(ErrorCodes.Field.Required)
            .MaximumLength(UnitLimits.MaxNameLength).WithErrorCode(ErrorCodes.Field.MaxLength);

        RuleFor(request => request.Code)
            .NotEmpty().WithErrorCode(ErrorCodes.Field.Required)
            .MaximumLength(UnitLimits.MaxCodeLength).WithErrorCode(ErrorCodes.Field.MaxLength);
    }
}

public sealed class RenameUnitRequestValidator : AbstractValidator<RenameUnitRequest>
{
    public RenameUnitRequestValidator()
    {
        RuleFor(request => request.NameAr)
            .NotEmpty().WithErrorCode(ErrorCodes.Field.Required)
            .MaximumLength(UnitLimits.MaxNameLength).WithErrorCode(ErrorCodes.Field.MaxLength);

        RuleFor(request => request.NameEn)
            .NotEmpty().WithErrorCode(ErrorCodes.Field.Required)
            .MaximumLength(UnitLimits.MaxNameLength).WithErrorCode(ErrorCodes.Field.MaxLength);
    }
}

public sealed class MoveTeamRequestValidator : AbstractValidator<MoveTeamRequest>
{
    public MoveTeamRequestValidator()
    {
        RuleFor(request => request.DepartmentId)
            .NotEmpty().WithErrorCode(ErrorCodes.Field.Required);
    }
}
