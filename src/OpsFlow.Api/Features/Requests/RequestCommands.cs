using Microsoft.AspNetCore.Http.HttpResults;
using Microsoft.Data.SqlClient;
using Microsoft.EntityFrameworkCore;
using OpsFlow.Api.Infrastructure.Errors;
using OpsFlow.Api.Infrastructure.Http;
using OpsFlow.Api.Persistence;
using OpsFlow.Domain.Requests;

namespace OpsFlow.Api.Features.Requests;

internal sealed record LoadedRequest(Request? Request, ProblemHttpResult? Problem);

/// <summary>The steps shared by every state-changing endpoint on an existing request.</summary>
internal static class RequestCommands
{
    /// <summary>
    /// Requires If-Match (428), applies visibility (404), compares the ETag (412) and, when asked,
    /// loads the current round's steps into the aggregate. Earlier rounds are never loaded for commands.
    /// Together with <see cref="SaveAggregateAsync"/>, a stale writer always gets 412, never a misleading domain error.
    /// </summary>
    public static async Task<LoadedRequest> LoadForCommandAsync(
        this OpsFlowDbContext db,
        long requestId,
        Actor actor,
        string? ifMatch,
        bool withCurrentRoundSteps,
        CancellationToken cancellationToken)
    {
        if (string.IsNullOrWhiteSpace(ifMatch))
        {
            return new LoadedRequest(null, Problems.IfMatchRequired());
        }

        // Children are read BEFORE the root. Every write changes the root in the same transaction as
        // its children, so a commit landing between the two reads changes the root's rowversion and is
        // caught by the ETag check below; a commit after both reads is caught by SaveChanges. Reading the
        // root first could pair an old root with newer steps and yield a misleading domain error.
        if (withCurrentRoundSteps)
        {
            await db.ApprovalSteps
                .Where(s => s.RequestId == requestId
                    && s.Round == db.Requests.Where(r => r.Id == requestId).Select(r => r.CurrentRound).FirstOrDefault())
                .LoadAsync(cancellationToken);
        }

        var request = await db.Requests.VisibleTo(actor, db).SingleOrDefaultAsync(r => r.Id == requestId, cancellationToken);
        if (request is null)
        {
            return new LoadedRequest(null, Problems.RequestNotFound());
        }

        // Fast path for stale clients. The race between this check and SaveChanges is closed by the
        // rowversion predicate EF adds to the UPDATE (DbUpdateConcurrencyException → 412).
        if (!ETags.Matches(ifMatch, request.RowVersion))
        {
            return new LoadedRequest(null, Problems.ETagMismatch());
        }

        return new LoadedRequest(request, null);
    }

    /// <summary>
    /// Saves an aggregate change, mapping lost races to 412. Two writers can collide in two ways:
    /// on the root's rowversion (DbUpdateConcurrencyException), or — when both insert the same child
    /// rows, e.g. two simultaneous submissions creating round N — on the unique index of the steps,
    /// which SQL Server may hit before it reaches the rowversion check. Both mean "someone else
    /// changed this request first", and nothing from the losing transaction is kept.
    /// </summary>
    public static async Task<ProblemHttpResult?> SaveAggregateAsync(this OpsFlowDbContext db, CancellationToken cancellationToken)
    {
        try
        {
            await db.SaveChangesAsync(cancellationToken);
            return null;
        }
        catch (DbUpdateException exception) when (exception is DbUpdateConcurrencyException || IsUniqueKeyViolation(exception))
        {
            return Problems.ETagMismatch();
        }
    }

    private static bool IsUniqueKeyViolation(DbUpdateException exception) =>
        exception.InnerException is SqlException { Number: 2601 or 2627 };

    /// <summary>Re-reads the request after a change and returns it with its new ETag.</summary>
    public static async Task<Ok<RequestDetailsResponse>> DetailsAfterChangeAsync(
        this OpsFlowDbContext db,
        long requestId,
        Actor actor,
        HttpResponse response,
        CancellationToken cancellationToken)
    {
        var details = await db.GetDetailsAsync(requestId, actor, cancellationToken)
            ?? throw new InvalidOperationException("A request must remain visible to the user who just changed it.");

        response.SetETag(details.RowVersion);
        return TypedResults.Ok(details.Response);
    }
}
