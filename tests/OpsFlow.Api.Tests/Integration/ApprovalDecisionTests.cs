using System.Net;
using Microsoft.EntityFrameworkCore;
using OpsFlow.Api.Features.Requests;
using OpsFlow.Domain.Auditing;
using OpsFlow.Domain.Requests;
using static OpsFlow.Api.Tests.Integration.TestUsers;

namespace OpsFlow.Api.Tests.Integration;

[TestFixture]
public class ApprovalDecisionTests : IntegrationTestBase
{
    [Test]
    public async Task Approve_SingleStep_ApprovesRequest()
    {
        var (submitted, etag) = await SubmittedRequestAsync(Employee1, 1_200m);
        using var manager = ClientFor(Manager1);

        using var response = await DecideAsync(manager, submitted.Id, submitted.Steps[0].Id, etag, "Approve", "Looks good");

        Assert.That(response.StatusCode, Is.EqualTo(HttpStatusCode.OK), await response.Content.ReadAsStringAsync());
        var body = await ReadAsync<RequestDetailsResponse>(response);
        Assert.Multiple(() =>
        {
            Assert.That(body.Status, Is.EqualTo(RequestStatus.Approved));
            Assert.That(body.Steps[0].Status, Is.EqualTo(StepStatus.Approved));
            Assert.That(body.Steps[0].DecidedBy, Is.EqualTo(new UserSummary(Manager1.Id, Manager1.DisplayName)));
            Assert.That(body.Steps[0].Comment, Is.EqualTo("Looks good"));
            Assert.That(response.Headers.ETag?.Tag, Is.Not.EqualTo(etag));
        });
    }

    [Test]
    public async Task Approve_FullChain_ApprovesAfterManagerFinanceAndDirector()
    {
        var (submitted, _) = await SubmittedRequestAsync(Employee3, 75_000m);

        await DecideCurrentStepAsync(Manager2, submitted.Id);
        await DecideCurrentStepAsync(Finance1, submitted.Id);
        var (final, _) = await DecideCurrentStepAsync(Director, submitted.Id);

        Assert.Multiple(() =>
        {
            Assert.That(final.Status, Is.EqualTo(RequestStatus.Approved));
            Assert.That(final.Steps.Select(s => s.DecidedBy?.Id), Is.EqualTo(new int?[] { Manager2.Id, Finance1.Id, Director.Id }));
            Assert.That(final.Steps[1].Assignee?.Id, Is.EqualTo(Finance1.Id), "BR-25: deciding a pool step assigns it");
        });
    }

    [Test]
    public async Task Decide_LaterStepFirst_Returns409NotCurrent()
    {
        var (submitted, etag) = await SubmittedRequestAsync(Employee3, 75_000m);
        using var finance = ClientFor(Finance1);

        using var response = await DecideAsync(finance, submitted.Id, submitted.Steps[1].Id, etag, "Approve");

        Assert.That(response.StatusCode, Is.EqualTo(HttpStatusCode.Conflict));
        Assert.That((await ReadProblemAsync(response)).Code, Is.EqualTo("step.not_current"));
    }

    [Test]
    public async Task Decide_OwnRequestAtFinanceStep_Returns403SelfApproval_ButColleagueCanApprove()
    {
        var (submitted, _) = await SubmittedRequestAsync(Finance1, 7_000m);
        var (afterManager, etag) = await DecideCurrentStepAsync(Director, submitted.Id);
        using var self = ClientFor(Finance1);

        using var response = await DecideAsync(self, submitted.Id, afterManager.Steps[1].Id, etag, "Approve");

        Assert.That(response.StatusCode, Is.EqualTo(HttpStatusCode.Forbidden));
        Assert.That((await ReadProblemAsync(response)).Code, Is.EqualTo("approval.self_approval"));
        var (approved, _) = await DecideCurrentStepAsync(Finance2, submitted.Id);
        Assert.That(approved.Status, Is.EqualTo(RequestStatus.Approved));
    }

    [Test]
    public async Task Decide_SecondStepOfSameRoundBySameUser_Returns403DuplicateApprover()
    {
        // Manager1 reports to the Director, who therefore decides both the Manager and Director steps.
        var (submitted, _) = await SubmittedRequestAsync(Manager1, 60_000m);
        await DecideCurrentStepAsync(Director, submitted.Id);
        var (afterFinance, etag) = await DecideCurrentStepAsync(Finance1, submitted.Id);
        using var director = ClientFor(Director);

        using var response = await DecideAsync(director, submitted.Id, afterFinance.Steps[2].Id, etag, "Approve");

        Assert.That(response.StatusCode, Is.EqualTo(HttpStatusCode.Forbidden));
        Assert.That((await ReadProblemAsync(response)).Code, Is.EqualTo("approval.duplicate_approver"));
    }

    [Test]
    public async Task Decide_StepAssignedToSomeoneElse_Returns403NotAssignee()
    {
        var (submitted, etag) = await SubmittedRequestAsync(Employee1);
        using var director = ClientFor(Director); // can see everything, but the step belongs to Manager1

        using var response = await DecideAsync(director, submitted.Id, submitted.Steps[0].Id, etag, "Approve");

        Assert.That(response.StatusCode, Is.EqualTo(HttpStatusCode.Forbidden));
        Assert.That((await ReadProblemAsync(response)).Code, Is.EqualTo("step.not_assignee"));
    }

    [Test]
    public async Task Decide_PoolStepWithWrongRole_Returns403NotEligible()
    {
        var (submitted, _) = await SubmittedRequestAsync(Employee3, 75_000m);
        var (afterManager, etag) = await DecideCurrentStepAsync(Manager2, submitted.Id);
        using var director = ClientFor(Director);

        using var response = await DecideAsync(director, submitted.Id, afterManager.Steps[1].Id, etag, "Approve");

        Assert.That(response.StatusCode, Is.EqualTo(HttpStatusCode.Forbidden));
        Assert.That((await ReadProblemAsync(response)).Code, Is.EqualTo("step.not_eligible"));
    }

    [Test]
    public async Task Decide_ByUserWhoCannotSeeTheRequest_Returns404()
    {
        var (submitted, etag) = await SubmittedRequestAsync(Employee1);
        using var otherManager = ClientFor(Manager2);

        using var response = await DecideAsync(otherManager, submitted.Id, submitted.Steps[0].Id, etag, "Approve");

        Assert.That(response.StatusCode, Is.EqualTo(HttpStatusCode.NotFound));
    }

    [Test]
    public async Task Reject_WithoutComment_Returns400()
    {
        var (submitted, etag) = await SubmittedRequestAsync(Employee1);
        using var manager = ClientFor(Manager1);

        using var response = await DecideAsync(manager, submitted.Id, submitted.Steps[0].Id, etag, "Reject");

        Assert.That(response.StatusCode, Is.EqualTo(HttpStatusCode.BadRequest));
        var problem = await ReadProblemAsync(response);
        Assert.That(problem.Errors!.Keys, Is.EquivalentTo(new[] { "comment" }));
    }

    [Test]
    public async Task Reject_WithComment_RejectsAndSkipsRemainingSteps()
    {
        var (submitted, etag) = await SubmittedRequestAsync(Employee3, 75_000m);
        using var manager = ClientFor(Manager2);

        using var response = await DecideAsync(manager, submitted.Id, submitted.Steps[0].Id, etag, "Reject", "Not this quarter");

        var body = await ReadAsync<RequestDetailsResponse>(response);
        Assert.Multiple(() =>
        {
            Assert.That(body.Status, Is.EqualTo(RequestStatus.Rejected));
            Assert.That(body.CompletedAt, Is.Not.Null);
            Assert.That(body.Steps.Select(s => s.Status), Is.EqualTo(new[] { StepStatus.Rejected, StepStatus.Skipped, StepStatus.Skipped }));
        });
    }

    [Test]
    public async Task Decide_WithoutIfMatch_Returns428()
    {
        var (submitted, _) = await SubmittedRequestAsync(Employee1);
        using var manager = ClientFor(Manager1);

        using var response = await DecideAsync(manager, submitted.Id, submitted.Steps[0].Id, etag: null, "Approve");

        Assert.That(response.StatusCode, Is.EqualTo(HttpStatusCode.PreconditionRequired));
    }

    [Test]
    public async Task Decide_UnknownStep_Returns404StepNotFound()
    {
        var (submitted, etag) = await SubmittedRequestAsync(Employee1);
        using var manager = ClientFor(Manager1);

        using var response = await DecideAsync(manager, submitted.Id, 999_999, etag, "Approve");

        Assert.That(response.StatusCode, Is.EqualTo(HttpStatusCode.NotFound));
        Assert.That((await ReadProblemAsync(response)).Code, Is.EqualTo("step.not_found"));
    }

    [Test]
    public async Task Decide_OnDraft_Returns409InvalidTransition()
    {
        var (draft, etag) = await CreateRequestAsync(Employee1);
        using var manager = ClientFor(Manager1);

        using var response = await DecideAsync(manager, draft.Id, 1, etag, "Approve");

        Assert.That(response.StatusCode, Is.EqualTo(HttpStatusCode.Conflict));
        Assert.That((await ReadProblemAsync(response)).Code, Is.EqualTo("request.invalid_transition"));
    }

    [Test]
    public async Task RequestChanges_EditAndResubmit_StartsRound2FromNewAmount()
    {
        var (submitted, _) = await SubmittedRequestAsync(Employee3, 75_000m);
        var (returned, etag) = await DecideCurrentStepAsync(Manager2, submitted.Id, "RequestChanges", "Please reduce the scope");
        Assert.That(returned.Status, Is.EqualTo(RequestStatus.ChangesRequested));
        using var requester = ClientFor(Employee3);

        using var updated = await UpdateAsync(requester, submitted.Id, etag,
            new { title = "Smaller rack upgrade", category = "Purchase", amount = 3_000m });
        Assert.That(updated.StatusCode, Is.EqualTo(HttpStatusCode.OK), await updated.Content.ReadAsStringAsync());
        using var resubmitted = await SubmitAsync(requester, submitted.Id, updated.Headers.ETag!.Tag);

        var body = await ReadAsync<RequestDetailsResponse>(resubmitted);
        Assert.Multiple(async () =>
        {
            Assert.That(body.Status, Is.EqualTo(RequestStatus.InApproval));
            Assert.That(body.CurrentRound, Is.EqualTo(2));
            Assert.That(body.Steps.Select(s => (s.Round, s.Kind)), Is.EqualTo(new[] { (2, StepKind.Manager) }));
            Assert.That(await QueryDbAsync(db => db.ApprovalSteps.CountAsync(s => s.RequestId == submitted.Id && s.Round == 1)),
                Is.EqualTo(3), "round 1 is kept for history");
            Assert.That(await QueryDbAsync(db => db.AuditEntries.Where(a => a.RequestId == submitted.Id).OrderBy(a => a.Id).Select(a => a.Action).ToListAsync()),
                Is.EqualTo(new[]
                {
                    AuditActions.RequestCreated, AuditActions.RequestSubmitted, AuditActions.ChangesRequested,
                    AuditActions.RequestUpdated, AuditActions.RequestSubmitted,
                }));
        });
    }

    // AC-12: two eligible users race for the same pool step with the same ETag.
    [Test]
    [Repeat(20)] // races are timing-dependent: repeat so an intermittent failure cannot hide
    public async Task Decide_SamePoolStepConcurrentlyByTwoFinanceUsers_ExactlyOneSucceeds()
    {
        var (submitted, _) = await SubmittedRequestAsync(Employee3, 7_000m);
        var (afterManager, etag) = await DecideCurrentStepAsync(Manager2, submitted.Id);
        var financeStepId = afterManager.Steps[1].Id;
        using var finance1 = ClientFor(Finance1);
        using var finance2 = ClientFor(Finance2);

        var responses = await Task.WhenAll(
            DecideAsync(finance1, submitted.Id, financeStepId, etag, "Approve"),
            DecideAsync(finance2, submitted.Id, financeStepId, etag, "Reject", "Over budget"));

        try
        {
            Assert.That(responses.Select(r => r.StatusCode),
                Is.EquivalentTo(new[] { HttpStatusCode.OK, HttpStatusCode.PreconditionFailed }));
            var decisions = await QueryDbAsync(db => db.AuditEntries.CountAsync(a =>
                a.RequestId == submitted.Id && (a.Action == AuditActions.StepApproved || a.Action == AuditActions.StepRejected)));
            Assert.That(decisions, Is.EqualTo(2), "manager decision + exactly one finance decision");
        }
        finally
        {
            foreach (var response in responses)
            {
                response.Dispose();
            }
        }
    }
}
