using System.Security.Claims;
using Microsoft.AspNetCore.Http.HttpResults;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using OpsFlow.Api.Infrastructure.Auth;
using OpsFlow.Api.Infrastructure.Http;
using OpsFlow.Api.Infrastructure.Idempotency;
using OpsFlow.Api.Persistence;
using OpsFlow.Domain.Requests;
using OpsFlow.Domain.Users;

namespace OpsFlow.Api.Features.Requests;

public static class SubmitRequestEndpoint
{
    public static RouteHandlerBuilder MapSubmitRequest(this IEndpointRouteBuilder routes) =>
        routes.MapPost("/{id:long}/submit", SubmitAsync)
            .WithIdempotency()
            .WithName("SubmitRequest")
            .WithSummary("Submit a draft, or resubmit a request returned for changes (starts a new round). Requires If-Match.");

    private static Task<Results<Ok<RequestDetailsResponse>, ProblemHttpResult>> SubmitAsync(
        long id,
        [FromHeader(Name = ETags.IfMatchHeader)] string? ifMatch,
        ClaimsPrincipal principal,
        OpsFlowDbContext db,
        TimeProvider clock,
        HttpResponse response,
        CancellationToken cancellationToken)
    {
        var actor = principal.ToActor();
        return db.ExecuteAsync(id, actor, ifMatch, withCurrentRoundSteps: false, async request =>
        {
            // The domain checks the manager's role; inactive managers are treated as missing (BR-12).
            var manager = await db.Users
                .Where(u => u.Id == request.RequesterId)
                .Select(u => db.Users.Where(m => m.Id == u.ManagerId && m.IsActive).Select(m => new { m.Id, m.Role }).FirstOrDefault())
                .SingleAsync(cancellationToken);
            var approvers = await db.Users
                .Where(u => u.IsActive && (u.Role == UserRole.Finance || u.Role == UserRole.Director))
                .Select(u => new Actor(u.Id, u.Role))
                .ToListAsync(cancellationToken);
            var policy = new ApprovalPolicy(await db.ApprovalPolicyRules.AsNoTracking().ToListAsync(cancellationToken));

            return request.Submit(
                actor, manager is null ? null : new Actor(manager.Id, manager.Role), policy, approvers, clock.GetUtcNow());
        }, response, cancellationToken);
    }
}
