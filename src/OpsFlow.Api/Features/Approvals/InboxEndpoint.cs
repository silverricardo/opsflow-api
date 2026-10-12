using System.Security.Claims;
using Microsoft.AspNetCore.Http.HttpResults;
using Microsoft.EntityFrameworkCore;
using OpsFlow.Api.Features.Common;
using OpsFlow.Api.Features.Requests;
using OpsFlow.Api.Infrastructure.Auth;
using OpsFlow.Api.Infrastructure.Validation;
using OpsFlow.Api.Persistence;
using OpsFlow.Domain.Requests;

namespace OpsFlow.Api.Features.Approvals;

public sealed record InboxItemResponse(
    long RequestId,
    string Reference,
    string Title,
    RequestCategory Category,
    decimal Amount,
    UserSummary Requester,
    long StepId,
    StepKind StepKind,
    int Round,
    bool AssignedToMe,
    DateTimeOffset? SubmittedAt);

public static class InboxEndpoint
{
    public static void MapInboxEndpoint(this IEndpointRouteBuilder routes) =>
        routes.MapGet("/approvals/inbox", GetInboxAsync)
            .RequireAuthorization(Policies.Approver)
            .WithValidation<PageQuery>()
            .WithName("GetApprovalInbox")
            .WithTags("Approvals")
            .WithSummary("Steps the caller can decide now: assigned to them, or unassigned in their role's pool. Oldest first.");

    /// <summary>
    /// Mirrors the domain's decision rules in SQL (AC-20): only the current step of a request in approval
    /// (<c>IsCurrent</c>); never the caller's own requests; never a round they already decided in.
    /// Served by the filtered indexes on current steps.
    /// </summary>
    private static async Task<Ok<PagedResponse<InboxItemResponse>>> GetInboxAsync(
        [AsParameters] PageQuery page,
        ClaimsPrincipal principal,
        OpsFlowDbContext db,
        CancellationToken cancellationToken)
    {
        var actor = principal.ToActor();
        var me = actor.UserId;
        var poolKinds = Enum.GetValues<StepKind>().Where(kind => ApprovalStep.IsEligibleRole(kind, actor.Role)).ToList();

        // Only current steps are candidates: one per request in approval, maintained by the domain. Two
        // branches ("assigned to me" / "unassigned in my pool"), each a seek on a filtered index of current
        // steps. This replaced a "no earlier pending step" check per pending step (~16k reads per COUNT).
        var current = db.ApprovalSteps.Where(s => s.IsCurrent);
        var candidates = current.Where(s => s.AssigneeId == me)
            .Concat(current.Where(s => s.AssigneeId == null && poolKinds.Contains(s.Kind)));

        var inbox =
            from step in candidates
            join request in db.Requests on step.RequestId equals request.Id
            where request.RequesterId != me
                && !db.ApprovalSteps.Any(decided => decided.RequestId == step.RequestId && decided.Round == step.Round
                    && decided.DecidedById == me)
            select new { step, request };

        var totalCount = await inbox.CountAsync(cancellationToken);
        var items = await inbox
            .OrderBy(x => x.request.SubmittedAt)
            .ThenBy(x => x.step.Id)
            .Skip(page.Skip)
            .Take(page.Size)
            .Select(x => new InboxItemResponse(
                x.request.Id,
                x.request.Reference,
                x.request.Title,
                x.request.Category,
                x.request.Amount,
                db.Users.Where(u => u.Id == x.request.RequesterId).Select(u => new UserSummary(u.Id, u.DisplayName)).First(),
                x.step.Id,
                x.step.Kind,
                x.step.Round,
                x.step.AssigneeId == me,
                x.request.SubmittedAt))
            .ToListAsync(cancellationToken);

        return TypedResults.Ok(PagedResponse.Create(items, page, totalCount));
    }
}
