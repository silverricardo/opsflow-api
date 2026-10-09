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
    public static IQueryable<Request> VisibleTo(this IQueryable<Request> requests, Actor actor, IQueryable<User> users)
    {
        if (actor.Role is UserRole.Finance or UserRole.Director or UserRole.Admin)
        {
            return requests;
        }

        var userId = actor.UserId;
        return requests.Where(r =>
            r.RequesterId == userId
            || r.Steps.Any(s => s.AssigneeId == userId || s.DecidedById == userId)
            || users.Any(u => u.Id == r.RequesterId && u.ManagerId == userId));
    }

    /// <summary>One round trip: the request, its current-round steps and the display names they reference.</summary>
    public static Task<RequestDetailsRow?> GetDetailsAsync(
        this OpsFlowDbContext db,
        long requestId,
        Actor actor,
        CancellationToken cancellationToken) =>
        db.Requests
            .VisibleTo(actor, db.Users)
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
