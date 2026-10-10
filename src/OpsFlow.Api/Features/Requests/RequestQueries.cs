using Microsoft.EntityFrameworkCore;
using OpsFlow.Api.Persistence;
using OpsFlow.Domain.Requests;
using OpsFlow.Domain.Users;

namespace OpsFlow.Api.Features.Requests;

internal sealed record RequestDetailsRow(RequestDetailsResponse Response, byte[] RowVersion);

internal static class RequestQueries
{
    /// <summary>
    /// Visibility rules BR-40/BR-41, applied in SQL. Requests outside this set are reported as
    /// not found (BR-42), so callers cannot probe which IDs exist.
    /// </summary>
    /// <remarks>
    /// Written as <c>Id IN (… UNION …)</c> on purpose. The equivalent <c>OR</c> of three conditions defeated
    /// every index: the baseline load test measured ~740k logical reads per COUNT for an employee. Each
    /// branch here is an index seek (see perf/README.md).
    /// </remarks>
    public static IQueryable<Request> VisibleTo(this IQueryable<Request> requests, Actor actor, OpsFlowDbContext db)
    {
        ArgumentNullException.ThrowIfNull(db);
        if (actor.Role is UserRole.Finance or UserRole.Director or UserRole.Admin)
        {
            return requests;
        }

        var userId = actor.UserId;
        var visibleIds = db.Requests.Where(r => r.RequesterId == userId).Select(r => r.Id)
            .Union(db.ApprovalSteps.Where(s => s.AssigneeId == userId).Select(s => s.RequestId))
            .Union(db.ApprovalSteps.Where(s => s.DecidedById == userId).Select(s => s.RequestId))
            .Union(
                from report in db.Users
                where report.ManagerId == userId
                join r in db.Requests on report.Id equals r.RequesterId
                select r.Id);

        return requests.Where(r => visibleIds.Contains(r.Id));
    }

    /// <summary>One round trip: the request, its current-round steps and the display names they reference.</summary>
    public static Task<RequestDetailsRow?> GetDetailsAsync(
        this OpsFlowDbContext db,
        long requestId,
        Actor actor,
        CancellationToken cancellationToken) =>
        db.Requests
            .VisibleTo(actor, db)
            .Where(r => r.Id == requestId)
            .Select(r => new RequestDetailsRow(
                new RequestDetailsResponse(
                    r.Id,
                    r.Reference,
                    r.Title,
                    r.Description,
                    r.Category,
                    r.Amount,
                    r.Status,
                    db.Users.Where(u => u.Id == r.RequesterId).Select(u => new UserSummary(u.Id, u.DisplayName)).First(),
                    r.CurrentRound,
                    r.CreatedAt,
                    r.UpdatedAt,
                    r.SubmittedAt,
                    r.CompletedAt,
                    r.Steps
                        .Where(s => s.Round == r.CurrentRound)
                        .OrderBy(s => s.StepOrder)
                        .Select(s => new ApprovalStepResponse(
                            s.Id,
                            s.Round,
                            s.StepOrder,
                            s.Kind,
                            s.Status,
                            db.Users.Where(u => u.Id == s.AssigneeId).Select(u => new UserSummary(u.Id, u.DisplayName)).FirstOrDefault(),
                            db.Users.Where(u => u.Id == s.DecidedById).Select(u => new UserSummary(u.Id, u.DisplayName)).FirstOrDefault(),
                            s.DecidedAt,
                            s.Comment))
                        .ToList()),
                r.RowVersion))
            .SingleOrDefaultAsync(cancellationToken);
}
