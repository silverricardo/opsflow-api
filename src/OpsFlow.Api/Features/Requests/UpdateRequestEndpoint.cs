using System.Security.Claims;
using Microsoft.AspNetCore.Http.HttpResults;
using Microsoft.AspNetCore.Mvc;
using OpsFlow.Api.Infrastructure.Auth;
using OpsFlow.Api.Infrastructure.Http;
using OpsFlow.Api.Infrastructure.Validation;
using OpsFlow.Api.Persistence;

namespace OpsFlow.Api.Features.Requests;

public static class UpdateRequestEndpoint
{
    public static RouteHandlerBuilder MapUpdateRequest(this IEndpointRouteBuilder routes) =>
        routes.MapPut("/{id:long}", UpdateAsync)
            .WithValidation<RequestDetailsBody>()
            .WithName("UpdateRequest")
            .WithSummary("Edit a draft or a request returned for changes. Requires If-Match.");

    private static Task<Results<Ok<RequestDetailsResponse>, ProblemHttpResult>> UpdateAsync(
        long id,
        RequestDetailsBody body,
        [FromHeader(Name = ETags.IfMatchHeader)] string? ifMatch,
        ClaimsPrincipal principal,
        OpsFlowDbContext db,
        TimeProvider clock,
        HttpResponse response,
        CancellationToken cancellationToken)
    {
        var actor = principal.ToActor();
        return db.ExecuteAsync(id, actor, ifMatch, withCurrentRoundSteps: false, request =>
            Task.FromResult(request.Update(actor, body.Title!, body.Description, body.Category!.Value, body.Amount!.Value, clock.GetUtcNow())),
            response, cancellationToken);
    }
}
