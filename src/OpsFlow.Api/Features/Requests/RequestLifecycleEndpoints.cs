using System.Security.Claims;
using Microsoft.AspNetCore.Mvc;
using OpsFlow.Api.Infrastructure.Auth;
using OpsFlow.Api.Infrastructure.Http;
using OpsFlow.Api.Infrastructure.Idempotency;
using OpsFlow.Api.Persistence;

namespace OpsFlow.Api.Features.Requests;

public static class RequestLifecycleEndpoints
{
    public static RouteHandlerBuilder MapCancelRequest(this IEndpointRouteBuilder routes) =>
        routes.MapPost("/{id:long}/cancel", (long id, [FromHeader(Name = ETags.IfMatchHeader)] string? ifMatch, ClaimsPrincipal principal,
                OpsFlowDbContext db, TimeProvider clock, HttpResponse response, CancellationToken ct) =>
                db.ExecuteAsync(id, principal.ToActor(), ifMatch, withCurrentRoundSteps: true,
                    request => Task.FromResult(request.Cancel(principal.ToActor(), clock.GetUtcNow())), response, ct))
            .WithIdempotency()
            .WithName("CancelRequest")
            .WithSummary("The requester withdraws a draft, in-approval or returned request. Requires If-Match.");

    public static RouteHandlerBuilder MapFulfilRequest(this IEndpointRouteBuilder routes) =>
        routes.MapPost("/{id:long}/fulfil", (long id, [FromHeader(Name = ETags.IfMatchHeader)] string? ifMatch, ClaimsPrincipal principal,
                OpsFlowDbContext db, TimeProvider clock, HttpResponse response, CancellationToken ct) =>
                db.ExecuteAsync(id, principal.ToActor(), ifMatch, withCurrentRoundSteps: false,
                    request => Task.FromResult(request.Fulfil(principal.ToActor(), clock.GetUtcNow())), response, ct))
            .WithIdempotency()
            .RequireAuthorization(Policies.FinanceOnly)
            .WithName("FulfilRequest")
            .WithSummary("Finance: record that an approved request was fulfilled. Requires If-Match.");
}
