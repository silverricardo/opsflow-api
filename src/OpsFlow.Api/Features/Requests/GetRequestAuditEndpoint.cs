using System.Security.Claims;
using System.Text.Json;
using Microsoft.AspNetCore.Http.HttpResults;
using Microsoft.EntityFrameworkCore;
using OpsFlow.Api.Features.Common;
using OpsFlow.Api.Infrastructure.Auth;
using OpsFlow.Api.Infrastructure.Errors;
using OpsFlow.Api.Infrastructure.Validation;
using OpsFlow.Api.Persistence;
using OpsFlow.Domain.Requests;

namespace OpsFlow.Api.Features.Requests;

public sealed record AuditEntryResponse(
    long Id,
    DateTimeOffset OccurredAt,
    string Action,
    UserSummary Actor,
    RequestStatus? FromStatus,
    RequestStatus? ToStatus,
    JsonElement? Details,
    string CorrelationId);

public static class GetRequestAuditEndpoint
{
    public static RouteHandlerBuilder MapGetRequestAudit(this IEndpointRouteBuilder routes) =>
        routes.MapGet("/{id:long}/audit", GetAuditAsync)
            .WithValidation<PageQuery>()
            .WithName("GetRequestAudit")
            .WithSummary("Audit history of a request, oldest first (BR-53).");

    private static async Task<Results<Ok<PagedResponse<AuditEntryResponse>>, ProblemHttpResult>> GetAuditAsync(
        long id,
        [AsParameters] PageQuery page,
        ClaimsPrincipal principal,
        OpsFlowDbContext db,
        CancellationToken cancellationToken)
    {
        var actor = principal.ToActor();
        if (!await db.Requests.VisibleTo(actor, db).AnyAsync(r => r.Id == id, cancellationToken))
        {
            return Problems.RequestNotFound();
        }

        var entries = db.AuditEntries.Where(a => a.RequestId == id);
        var totalCount = await entries.CountAsync(cancellationToken);
        var rows = await entries
            .OrderBy(a => a.OccurredAt)
            .ThenBy(a => a.Id)
            .Skip(page.Skip)
            .Take(page.Size)
            .Select(a => new
            {
                a.Id,
                a.OccurredAt,
                a.Action,
                Actor = db.Users.Where(u => u.Id == a.ActorId).Select(u => new UserSummary(u.Id, u.DisplayName)).First(),
                a.FromStatus,
                a.ToStatus,
                a.Details,
                CorrelationId = EF.Property<string>(a, OpsFlowDbContext.CorrelationIdProperty),
            })
            .ToListAsync(cancellationToken);

        var items = rows
            .Select(r => new AuditEntryResponse(
                r.Id,
                r.OccurredAt,
                r.Action,
                r.Actor,
                r.FromStatus,
                r.ToStatus,
                r.Details is null ? null : JsonDocument.Parse(r.Details).RootElement.Clone(),
                r.CorrelationId))
            .ToList();

        return TypedResults.Ok(PagedResponse.Create(items, page, totalCount));
    }
}
