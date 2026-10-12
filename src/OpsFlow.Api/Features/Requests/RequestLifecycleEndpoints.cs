using System.Security.Claims;
using Microsoft.AspNetCore.Http.HttpResults;
using Microsoft.AspNetCore.Mvc;
using OpsFlow.Api.Infrastructure.Auth;
using OpsFlow.Api.Infrastructure.Errors;
using OpsFlow.Api.Infrastructure.Http;
using OpsFlow.Api.Infrastructure.Idempotency;
using OpsFlow.Api.Persistence;
using OpsFlow.Domain.Common;
using OpsFlow.Domain.Requests;

namespace OpsFlow.Api.Features.Requests;

public static class RequestLifecycleEndpoints
{
    public static RouteHandlerBuilder MapCancelRequest(this IEndpointRouteBuilder routes) =>
        routes.MapPost("/{id:long}/cancel", (long id, [FromHeader(Name = ETags.IfMatchHeader)] string? ifMatch, ClaimsPrincipal principal,
                OpsFlowDbContext db, TimeProvider clock, HttpResponse response, CancellationToken ct) =>
                ApplyAsync(id, ifMatch, principal, db, response, withSteps: true, (r, a) => r.Cancel(a, clock.GetUtcNow()), ct))
            .WithIdempotency()
            .WithName("CancelRequest")
            .WithSummary("The requester withdraws a draft, in-approval or returned request. Requires If-Match.");

    public static RouteHandlerBuilder MapFulfilRequest(this IEndpointRouteBuilder routes) =>
        routes.MapPost("/{id:long}/fulfil", (long id, [FromHeader(Name = ETags.IfMatchHeader)] string? ifMatch, ClaimsPrincipal principal,
                OpsFlowDbContext db, TimeProvider clock, HttpResponse response, CancellationToken ct) =>
                ApplyAsync(id, ifMatch, principal, db, response, withSteps: false, (r, a) => r.Fulfil(a, clock.GetUtcNow()), ct))
            .WithIdempotency()
            .RequireAuthorization(Policies.FinanceOnly)
            .WithName("FulfilRequest")
            .WithSummary("Finance: record that an approved request was fulfilled. Requires If-Match.");

    /// <summary>Load → domain operation → save → respond, for operations that need no extra input.</summary>
    private static async Task<Results<Ok<RequestDetailsResponse>, ProblemHttpResult>> ApplyAsync(
        long id,
        string? ifMatch,
        ClaimsPrincipal principal,
        OpsFlowDbContext db,
        HttpResponse response,
        bool withSteps,
        Func<Request, Actor, Result> operation,
        CancellationToken cancellationToken)
    {
        var actor = principal.ToActor();
        var (request, problem) = await db.LoadForCommandAsync(id, actor, ifMatch, withSteps, cancellationToken);
        if (request is null)
        {
            return problem!;
        }

        var result = operation(request, actor);
        if (!result.IsSuccess)
        {
            return result.Error.ToProblem();
        }

        if (await db.SaveAggregateAsync(cancellationToken) is { } conflict)
        {
            return conflict;
        }

        return await db.DetailsAfterChangeAsync(id, actor, response, cancellationToken);
    }
}
