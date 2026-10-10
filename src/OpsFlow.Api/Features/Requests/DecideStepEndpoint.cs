using System.Security.Claims;
using FluentValidation;
using Microsoft.AspNetCore.Http.HttpResults;
using Microsoft.AspNetCore.Mvc;
using OpsFlow.Api.Infrastructure.Auth;
using OpsFlow.Api.Infrastructure.Errors;
using OpsFlow.Api.Infrastructure.Idempotency;
using OpsFlow.Api.Infrastructure.Http;
using OpsFlow.Api.Infrastructure.Validation;
using OpsFlow.Api.Persistence;
using OpsFlow.Domain.Requests;

namespace OpsFlow.Api.Features.Requests;

public sealed record DecisionBody(Decision? Decision, string? Comment);

public sealed class DecisionBodyValidator : AbstractValidator<DecisionBody>
{
    public DecisionBodyValidator()
    {
        RuleFor(x => x.Decision).NotNull().IsInEnum();
        RuleFor(x => x.Comment).MaximumLength(ApprovalStep.CommentMaxLength);
        RuleFor(x => x.Comment)
            .NotEmpty()
            .When(x => x.Decision is Decision.Reject or Decision.RequestChanges)
            .WithMessage("A comment is required when rejecting or requesting changes.");
    }
}

public static class DecideStepEndpoint
{
    public static RouteHandlerBuilder MapDecideStep(this IEndpointRouteBuilder routes) =>
        routes.MapPost("/{id:long}/steps/{stepId:long}/decision", DecideAsync)
            .WithIdempotency()
            .WithValidation<DecisionBody>()
            .WithName("DecideStep")
            .WithSummary("Approve, reject or request changes on the current approval step. Requires If-Match.");

    private static async Task<Results<Ok<RequestDetailsResponse>, ProblemHttpResult>> DecideAsync(
        long id,
        long stepId,
        DecisionBody body,
        [FromHeader(Name = ETags.IfMatchHeader)] string? ifMatch,
        ClaimsPrincipal principal,
        OpsFlowDbContext db,
        TimeProvider clock,
        HttpResponse response,
        CancellationToken cancellationToken)
    {
        var actor = principal.ToActor();
        var (request, problem) = await db.LoadForCommandAsync(id, actor, ifMatch, withCurrentRoundSteps: true, cancellationToken);
        if (request is null)
        {
            return problem!;
        }

        var decided = request.Decide(stepId, actor, body.Decision!.Value, body.Comment, clock.GetUtcNow());
        if (!decided.IsSuccess)
        {
            return decided.Error.ToProblem();
        }

        if (await db.SaveAggregateAsync(cancellationToken) is { } conflict)
        {
            return conflict;
        }

        return await db.DetailsAfterChangeAsync(id, actor, response, cancellationToken);
    }
}
