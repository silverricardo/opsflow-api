using System.Security.Claims;
using Microsoft.AspNetCore.Http.HttpResults;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using OpsFlow.Api.Infrastructure.Auth;
using OpsFlow.Api.Infrastructure.Errors;
using OpsFlow.Api.Infrastructure.Http;
using OpsFlow.Api.Persistence;
using OpsFlow.Domain.Requests;

namespace OpsFlow.Api.Features.Requests;

public static class SubmitRequestEndpoint
{
    public static RouteHandlerBuilder MapSubmitRequest(this IEndpointRouteBuilder routes) =>
        routes.MapPost("/{id:long}/submit", SubmitAsync)
            .WithName("SubmitRequest")
            .WithSummary("Submit a draft (or a request returned for changes) for approval. Requires If-Match.");

    private static async Task<Results<Ok<RequestDetailsResponse>, ProblemHttpResult>> SubmitAsync(
        long id,
        [FromHeader(Name = ETags.IfMatchHeader)] string? ifMatch,
        ClaimsPrincipal principal,
        OpsFlowDbContext db,
        TimeProvider clock,
        HttpResponse response,
        CancellationToken cancellationToken)
    {
        if (string.IsNullOrWhiteSpace(ifMatch))
        {
            return Problems.IfMatchRequired();
        }

        var actor = principal.ToActor();
        var request = await db.Requests.VisibleTo(actor, db.Users).SingleOrDefaultAsync(r => r.Id == id, cancellationToken);
        if (request is null)
        {
            return Problems.RequestNotFound();
        }

        // Fast path for stale clients. The race between this check and SaveChanges is closed by the
        // rowversion predicate EF adds to the UPDATE (DbUpdateConcurrencyException → 412).
        if (!ETags.Matches(ifMatch, request.RowVersion))
        {
            return Problems.ETagMismatch();
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

        await db.SaveChangesAsync(cancellationToken);

        var details = await db.GetDetailsAsync(id, actor, cancellationToken)
            ?? throw new InvalidOperationException("A submitted request must remain visible to its requester.");

        response.SetETag(details.RowVersion);
        return TypedResults.Ok(details.Response);
    }
}
