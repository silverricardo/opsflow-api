using System.Security.Claims;
using Microsoft.AspNetCore.Http.HttpResults;
using OpsFlow.Api.Infrastructure.Auth;
using OpsFlow.Api.Infrastructure.Errors;
using OpsFlow.Api.Infrastructure.Http;
using OpsFlow.Api.Persistence;

namespace OpsFlow.Api.Features.Requests;

public static class GetRequestEndpoint
{
    public static RouteHandlerBuilder MapGetRequest(this IEndpointRouteBuilder routes) =>
        routes.MapGet("/{id:long}", GetAsync)
            .WithName("GetRequest")
            .WithSummary("Get a request with its current approval round. Returns the ETag needed for changes.");

    private static async Task<Results<Ok<RequestDetailsResponse>, ProblemHttpResult>> GetAsync(
        long id,
        ClaimsPrincipal principal,
        OpsFlowDbContext db,
        HttpResponse response,
        CancellationToken cancellationToken)
    {
        var details = await db.GetDetailsAsync(id, principal.ToActor(), cancellationToken);
        if (details is null)
        {
            return Problems.RequestNotFound();
        }

        response.SetETag(details.RowVersion);
        return TypedResults.Ok(details.Response);
    }
}
