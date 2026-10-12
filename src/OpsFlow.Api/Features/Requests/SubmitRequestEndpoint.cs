using System.Security.Claims;
using Microsoft.AspNetCore.Http.HttpResults;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using OpsFlow.Api.Infrastructure.Auth;
using OpsFlow.Api.Infrastructure.Errors;
using OpsFlow.Api.Infrastructure.Idempotency;
using OpsFlow.Api.Infrastructure.Http;
using OpsFlow.Api.Persistence;
using OpsFlow.Domain.Requests;

namespace OpsFlow.Api.Features.Requests;

public static class SubmitRequestEndpoint
{
    public static RouteHandlerBuilder MapSubmitRequest(this IEndpointRouteBuilder routes) =>
        routes.MapPost("/{id:long}/submit", SubmitAsync)
            .WithIdempotency()
            .WithName("SubmitRequest")
            .WithSummary("Submit a draft, or resubmit a request returned for changes (starts a new round). Requires If-Match.");

    private static async Task<Results<Ok<RequestDetailsResponse>, ProblemHttpResult>> SubmitAsync(
        long id,
        [FromHeader(Name = ETags.IfMatchHeader)] string? ifMatch,
        ClaimsPrincipal principal,
        OpsFlowDbContext db,
        TimeProvider clock,
        HttpResponse response,
        CancellationToken cancellationToken)
    {
        var actor = principal.ToActor();
        var (request, problem) = await db.LoadForCommandAsync(id, actor, ifMatch, withCurrentRoundSteps: false, cancellationToken);
        if (request is null)
        {
            return problem!;
        }

        // An inactive manager cannot approve anything, so treat them as missing (BR-12).
        var managerId = await db.Users
            .Where(u => u.Id == request.RequesterId)
            .Select(u => db.Users.Where(m => m.Id == u.ManagerId && m.IsActive).Select(m => (int?)m.Id).FirstOrDefault())
            .SingleAsync(cancellationToken);
        var policy = new ApprovalPolicy(await db.ApprovalPolicyRules.AsNoTracking().ToListAsync(cancellationToken));

        var submitted = request.Submit(actor, managerId, policy, clock.GetUtcNow());
        if (!submitted.IsSuccess)
        {
            return submitted.Error.ToProblem();
        }

        if (await db.SaveAggregateAsync(cancellationToken) is { } conflict)
        {
            return conflict;
        }

        return await db.DetailsAfterChangeAsync(id, actor, response, cancellationToken);
    }
}
