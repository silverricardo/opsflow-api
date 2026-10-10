using System.Security.Claims;
using Microsoft.AspNetCore.Http.HttpResults;
using OpsFlow.Api.Infrastructure.Auth;
using OpsFlow.Api.Infrastructure.Errors;
using OpsFlow.Api.Infrastructure.Idempotency;
using OpsFlow.Api.Infrastructure.Http;
using OpsFlow.Api.Infrastructure.Validation;
using OpsFlow.Api.Persistence;
using OpsFlow.Domain.Requests;

namespace OpsFlow.Api.Features.Requests;

public static class CreateRequestEndpoint
{
    public static RouteHandlerBuilder MapCreateRequest(this IEndpointRouteBuilder routes) =>
        routes.MapPost("/", CreateAsync)
            .WithIdempotency()
            .WithValidation<RequestDetailsBody>()
            .WithName("CreateRequest")
            .WithSummary("Create a draft request.");

    private static async Task<Results<Created<RequestDetailsResponse>, ProblemHttpResult>> CreateAsync(
        RequestDetailsBody body,
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
