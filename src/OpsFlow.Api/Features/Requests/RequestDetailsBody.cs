using FluentValidation;
using OpsFlow.Domain.Requests;

namespace OpsFlow.Api.Features.Requests;

/// <summary>
/// Body for creating and editing a request. Fields are nullable so that missing values reach the
/// validator and produce field-level errors instead of silent defaults.
/// </summary>
public sealed record RequestDetailsBody(string? Title, string? Description, RequestCategory? Category, decimal? Amount);

public sealed class RequestDetailsBodyValidator : AbstractValidator<RequestDetailsBody>
{
    public RequestDetailsBodyValidator()
    {
        RuleLevelCascadeMode = CascadeMode.Stop;

        RuleFor(x => x.Title)
            .NotEmpty()
            .Must(t => t!.Trim().Length is >= Request.TitleMinLength and <= Request.TitleMaxLength)
            .WithMessage($"Title must be between {Request.TitleMinLength} and {Request.TitleMaxLength} characters.");
        RuleFor(x => x.Description).MaximumLength(Request.DescriptionMaxLength);
        RuleFor(x => x.Category).NotNull().IsInEnum();
        RuleFor(x => x.Amount)
            .NotNull()
            .GreaterThan(0)
            .LessThanOrEqualTo(Request.MaxAmount)
            .PrecisionScale(18, 2, ignoreTrailingZeros: true);
    }
}
