using System.Security.Claims;
using FluentValidation;
using Microsoft.AspNetCore.Http.HttpResults;
using OpsFlow.Api.Infrastructure.Auth;
using OpsFlow.Api.Infrastructure.Errors;
using OpsFlow.Api.Infrastructure.Http;
using OpsFlow.Api.Infrastructure.Validation;
using OpsFlow.Api.Persistence;
using OpsFlow.Domain.Requests;

namespace OpsFlow.Api.Features.Requests;

/// <summary>Fields are nullable so that missing values reach the validator and produce field-level errors.</summary>
public sealed record CreateRequestBody(string? Title, string? Description, RequestCategory? Category, decimal? Amount);

public sealed class CreateRequestBodyValidator : AbstractValidator<CreateRequestBody>
{
    public CreateRequestBodyValidator()
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

public static class CreateRequestEndpoint
{
    public static RouteHandlerBuilder MapCreateRequest(this IEndpointRouteBuilder routes) =>
        routes.MapPost("/", CreateAsync)
            .WithValidation<CreateRequestBody>()
            .WithName("CreateRequest")
            .WithSummary("Create a draft request.");

    private static async Task<Results<Created<RequestDetailsResponse>, ProblemHttpResult>> CreateAsync(
        CreateRequestBody body,
        ClaimsPrincipal principal,
        OpsFlowDbContext db,
        TimeProvider clock,
        HttpResponse response,
        CancellationToken cancellationToken)
    {
        var actor = principal.ToActor();
        var created = Request.Create(actor, body.Title!, body.Description, body.Category!.Value, body.Amount!.Value, clock.GetUtcNow());
        if (!created.IsSuccess)
        {
            return created.Error.ToProblem();
        }

        db.Requests.Add(created.Value);
        await db.SaveChangesAsync(cancellationToken);

        var details = await db.GetDetailsAsync(created.Value.Id, actor, cancellationToken)
            ?? throw new InvalidOperationException("A newly created request must be visible to its requester.");

        response.SetETag(details.RowVersion);
        return TypedResults.Created($"/api/v1/requests/{created.Value.Id}", details.Response);
    }
}
