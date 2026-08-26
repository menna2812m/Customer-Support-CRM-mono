using Crm.Application.Common;
using FluentValidation;

namespace Crm.Application.Diagnostics;

/// <summary>
/// Reference slice request (spec FR-051). Non-business, removable, and here only to prove the
/// validation pipeline and the error contract end to end.
/// </summary>
public sealed record EchoRequest
{
    public string Message { get; init; } = string.Empty;

    public int RepeatCount { get; init; } = 1;

    /// <summary>Exercises the shared collection-length limit (spec FR-055).</summary>
    public IReadOnlyList<string> Tags { get; init; } = [];
}

public sealed record EchoResponse(string Message, DateTimeOffset ReceivedAt, string CorrelationId);

/// <summary>
/// Rules live beside the use case in the Application layer, never in the controller
/// (Constitution I). Explicit error codes keep the response machine-readable, so a client maps
/// them to translated messages instead of showing server text (spec LR-003).
/// </summary>
public sealed class EchoRequestValidator : AbstractValidator<EchoRequest>
{
    public const int MaxMessageLength = 200;
    public const int MaxRepeatCount = 10;

    public EchoRequestValidator()
    {
        RuleFor(request => request.Message)
            .NotEmpty()
            .WithErrorCode(ErrorCodes.Field.Required)
            .WithMessage("Message is required.")
            .MaximumLength(MaxMessageLength)
            .WithErrorCode(ErrorCodes.Field.MaxLength)
            .WithMessage($"Message must be at most {MaxMessageLength} characters.");

        RuleFor(request => request.Tags).MaxItems();

        RuleFor(request => request.RepeatCount)
            .InclusiveBetween(1, MaxRepeatCount)
            .WithErrorCode(ErrorCodes.Field.Range)
            .WithMessage($"Repeat count must be between 1 and {MaxRepeatCount}.");
    }
}
