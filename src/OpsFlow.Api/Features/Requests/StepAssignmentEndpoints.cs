using System.Security.Claims;
using FluentValidation;
using Microsoft.AspNetCore.Http.HttpResults;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using OpsFlow.Api.Infrastructure.Auth;
using OpsFlow.Api.Infrastructure.Errors;
using OpsFlow.Api.Infrastructure.Http;
using OpsFlow.Api.Infrastructure.Idempotency;
using OpsFlow.Api.Infrastructure.Validation;
using OpsFlow.Api.Persistence;
using OpsFlow.Domain.Requests;

namespace OpsFlow.Api.Features.Requests;

public sealed record ReassignStepBody(int? UserId);

public sealed class ReassignStepBodyValidator : AbstractValidator<ReassignStepBody>
{
    public ReassignStepBodyValidator() => RuleFor(x => x.UserId).NotNull().GreaterThan(0);
}

public static class StepAssignmentEndpoints
{
    public static RouteHandlerBuilder MapClaimStep(this IEndpointRouteBuilder routes) =>
        routes.MapPost("/{id:long}/steps/{stepId:long}/claim", ClaimAsync)
            .WithIdempotency()
            .RequireAuthorization(Policies.Approver)
            .WithName("ClaimStep")
            .WithSummary("Take an unassigned approval step from your role's pool. Requires If-Match.");

    public static RouteHandlerBuilder MapReassignStep(this IEndpointRouteBuilder routes) =>
        routes.MapPut("/{id:long}/steps/{stepId:long}/assignee", ReassignAsync)
            .RequireAuthorization(Policies.AdminOnly)
            .WithValidation<ReassignStepBody>()
            .WithName("ReassignStep")
            .WithSummary("Administrators: move a pending approval step to another eligible user. Requires If-Match.");

    private static Task<Results<Ok<RequestDetailsResponse>, ProblemHttpResult>> ClaimAsync(
        long id,
        long stepId,
        [FromHeader(Name = ETags.IfMatchHeader)] string? ifMatch,
        ClaimsPrincipal principal,
        OpsFlowDbContext db,
        TimeProvider clock,
        HttpResponse response,
        CancellationToken cancellationToken)
    {
        var actor = principal.ToActor();
        return db.ExecuteAsync(id, actor, ifMatch, withCurrentRoundSteps: true, request =>
            Task.FromResult(request.ClaimStep(stepId, actor, clock.GetUtcNow())), response, cancellationToken);
    }

    private static async Task<Results<Ok<RequestDetailsResponse>, ProblemHttpResult>> ReassignAsync(
        long id,
        long stepId,
        ReassignStepBody body,
        [FromHeader(Name = ETags.IfMatchHeader)] string? ifMatch,
        ClaimsPrincipal principal,
        OpsFlowDbContext db,
        TimeProvider clock,
        HttpResponse response,
        CancellationToken cancellationToken)
    {
        var assignee = await db.Users
            .Where(u => u.Id == body.UserId)
            .Select(u => new { u.Id, u.Role, u.IsActive })
            .SingleOrDefaultAsync(cancellationToken);
        if (assignee is null)
        {
            return Problems.Create(StatusCodes.Status404NotFound, "user.not_found", "Not found", "The user does not exist.");
        }

        var admin = principal.ToActor();
        return await db.ExecuteAsync(id, admin, ifMatch, withCurrentRoundSteps: true, request =>
            Task.FromResult(request.ReassignStep(stepId, admin, new Actor(assignee.Id, assignee.Role), assignee.IsActive, clock.GetUtcNow())),
            response, cancellationToken);
    }
}
