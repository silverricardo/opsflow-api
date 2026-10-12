using OpsFlow.Domain.Auditing;
using OpsFlow.Domain.Common;
using OpsFlow.Domain.Requests;
using static OpsFlow.Domain.Tests.Requests.TestData;

namespace OpsFlow.Domain.Tests.Requests;

[TestFixture]
public class RequestDecideTests
{
    private static readonly DateTimeOffset Later = Now.AddHours(1);

    [Test]
    public void Approve_SingleStep_ApprovesRequest()
    {
        var request = SubmittedRequest(1_200m);
        var step = request.Step(StepKind.Manager);

        var result = request.Decide(step.Id, Manager, Decision.Approve, "  Fine by me ", Later);

        Assert.That(result.IsSuccess, Is.True, result.Error?.Code);
        Assert.Multiple(() =>
        {
            Assert.That(request.Status, Is.EqualTo(RequestStatus.Approved));
            Assert.That(request.CompletedAt, Is.Null, "Approved is not terminal: fulfilment follows");
            Assert.That(request.UpdatedAt, Is.EqualTo(Later));
            Assert.That(step.Status, Is.EqualTo(StepStatus.Approved));
            Assert.That(step.DecidedById, Is.EqualTo(Manager.UserId));
            Assert.That(step.DecidedAt, Is.EqualTo(Later));
            Assert.That(step.Comment, Is.EqualTo("Fine by me"));
        });
    }

    [Test]
    public void Approve_FullChain_ApprovesOnlyAfterLastStep()
    {
        var request = SubmittedRequest(75_000m);

        request.Decide(request.Step(StepKind.Manager).Id, Manager, Decision.Approve, null, Later);
        Assert.That(request.Status, Is.EqualTo(RequestStatus.InApproval));
        request.Decide(request.Step(StepKind.Finance).Id, Finance1, Decision.Approve, null, Later);
        Assert.That(request.Status, Is.EqualTo(RequestStatus.InApproval));
        var last = request.Decide(request.Step(StepKind.Director).Id, Director, Decision.Approve, null, Later);

        Assert.Multiple(() =>
        {
            Assert.That(last.IsSuccess, Is.True);
            Assert.That(request.Status, Is.EqualTo(RequestStatus.Approved));
            Assert.That(request.Step(StepKind.Finance).AssigneeId, Is.EqualTo(Finance1.UserId), "BR-25: deciding claims the pool step");
            Assert.That(request.AuditEntries.Select(a => a.Action), Is.EqualTo(new[]
            {
                AuditActions.RequestCreated, AuditActions.RequestSubmitted,
                AuditActions.StepApproved, AuditActions.StepApproved, AuditActions.StepApproved,
            }));
            Assert.That(request.AuditEntries[^1].ToStatus, Is.EqualTo(RequestStatus.Approved));
        });
    }

    [Test]
    public void Reject_EndsRequestAndSkipsRemainingSteps()
    {
        var request = SubmittedRequest(75_000m);

        var result = request.Decide(request.Step(StepKind.Manager).Id, Manager, Decision.Reject, "Not in budget", Later);

        Assert.Multiple(() =>
        {
            Assert.That(result.IsSuccess, Is.True);
            Assert.That(request.Status, Is.EqualTo(RequestStatus.Rejected));
            Assert.That(request.CompletedAt, Is.EqualTo(Later));
            Assert.That(request.Steps.Select(s => s.Status),
                Is.EqualTo(new[] { StepStatus.Rejected, StepStatus.Skipped, StepStatus.Skipped }));
            Assert.That(request.AuditEntries[^1].Action, Is.EqualTo(AuditActions.StepRejected));
            Assert.That(request.AuditEntries[^1].Details, Does.Contain("Not in budget"));
        });
    }

    [Test]
    public void RequestChanges_ReturnsRequestToRequesterAndSkipsRemainingSteps()
    {
        var request = SubmittedRequest(75_000m);
        request.Decide(request.Step(StepKind.Manager).Id, Manager, Decision.Approve, null, Later);

        var result = request.Decide(request.Step(StepKind.Finance).Id, Finance1, Decision.RequestChanges, "Split per quarter", Later);

        Assert.Multiple(() =>
        {
            Assert.That(result.IsSuccess, Is.True);
            Assert.That(request.Status, Is.EqualTo(RequestStatus.ChangesRequested));
            Assert.That(request.CompletedAt, Is.Null);
            Assert.That(request.Steps.Select(s => s.Status),
                Is.EqualTo(new[] { StepStatus.Approved, StepStatus.ChangesRequested, StepStatus.Skipped }));
        });
    }

    [Test]
    public void Resubmit_AfterChangesRequested_StartsNewRoundFromUpdatedAmount()
    {
        var request = SubmittedRequest(75_000m);
        request.Decide(request.Step(StepKind.Manager).Id, Manager, Decision.RequestChanges, "Too expensive", Later);

        Assert.That(request.Update(Employee, "Cheaper laptop", null, RequestCategory.Purchase, 3_000m, Later).IsSuccess, Is.True);
        Assert.That(request.Submit(Employee, Manager, DefaultPolicy(), Approvers, Later).IsSuccess, Is.True);
        AssignStepIds(request);

        Assert.Multiple(() =>
        {
            Assert.That(request.CurrentRound, Is.EqualTo(2));
            Assert.That(request.Status, Is.EqualTo(RequestStatus.InApproval));
            Assert.That(request.CurrentRoundSteps().Select(s => s.Kind), Is.EqualTo(new[] { StepKind.Manager }));
            Assert.That(request.Steps.Count(s => s.Round == 1), Is.EqualTo(3), "BR-16: earlier rounds are kept");
        });

        // A round-1 step can no longer be decided.
        var oldStepId = request.Steps.First(s => s.Round == 1).Id;
        Assert.That(request.Decide(oldStepId, Manager, Decision.Approve, null, Later).Error, Is.EqualTo(ApprovalErrors.StepNotFound));
    }

    [Test]
    public void Decide_LaterStepBeforeEarlierOne_IsNotCurrent()
    {
        var request = SubmittedRequest(75_000m);

        var result = request.Decide(request.Step(StepKind.Finance).Id, Finance1, Decision.Approve, null, Later);

        Assert.That(result.Error, Is.EqualTo(ApprovalErrors.StepNotCurrent));
    }

    [Test]
    public void Decide_AlreadyDecidedStep_IsConflict()
    {
        var request = SubmittedRequest(75_000m);
        var managerStep = request.Step(StepKind.Manager);
        request.Decide(managerStep.Id, Manager, Decision.Approve, null, Later);

        Assert.That(request.Decide(managerStep.Id, Manager, Decision.Approve, null, Later).Error,
            Is.EqualTo(ApprovalErrors.StepAlreadyDecided));
    }

    // BR-20: e.g. a Finance employee's own request reaching the Finance step.
    [Test]
    public void Decide_OwnRequest_IsSelfApproval()
    {
        var request = SubmittedRequest(7_000m, requester: Finance1, manager: Director);
        request.Decide(request.Step(StepKind.Manager).Id, Director, Decision.Approve, null, Later);

        var result = request.Decide(request.Step(StepKind.Finance).Id, Finance1, Decision.Approve, null, Later);

        Assert.That(result.Error, Is.EqualTo(ApprovalErrors.SelfApproval));
        Assert.That(request.Decide(request.Step(StepKind.Finance).Id, Finance2, Decision.Approve, null, Later).IsSuccess, Is.True);
    }

    // BR-21: the Director approved as line manager, so cannot also approve the Director step.
    [Test]
    public void Decide_SecondStepInSameRoundBySameUser_IsDuplicateApprover()
    {
        var request = SubmittedRequest(60_000m, requester: Manager, manager: Director);
        request.Decide(request.Step(StepKind.Manager).Id, Director, Decision.Approve, null, Later);
        request.Decide(request.Step(StepKind.Finance).Id, Finance1, Decision.Approve, null, Later);

        var result = request.Decide(request.Step(StepKind.Director).Id, Director, Decision.Approve, null, Later);

        Assert.Multiple(() =>
        {
            Assert.That(result.Error, Is.EqualTo(ApprovalErrors.DuplicateApprover));
            Assert.That(request.Status, Is.EqualTo(RequestStatus.InApproval));
        });
    }

    [Test]
    public void Decide_AssignedStepBySomeoneElse_IsNotAssignee()
    {
        var request = SubmittedRequest();

        Assert.That(request.Decide(request.Step(StepKind.Manager).Id, OtherManager, Decision.Approve, null, Later).Error,
            Is.EqualTo(ApprovalErrors.NotAssignee));
    }

    [Test]
    public void Decide_PoolStepByWrongRole_IsNotEligible()
    {
        var request = SubmittedRequest(75_000m);
        request.Decide(request.Step(StepKind.Manager).Id, Manager, Decision.Approve, null, Later);

        Assert.That(request.Decide(request.Step(StepKind.Finance).Id, Director, Decision.Approve, null, Later).Error,
            Is.EqualTo(ApprovalErrors.NotEligible));
    }

    [TestCase(Decision.Reject, null)]
    [TestCase(Decision.Reject, "   ")]
    [TestCase(Decision.RequestChanges, "")]
    public void Decide_RejectOrRequestChangesWithoutComment_IsValidationError(Decision decision, string? comment)
    {
        var request = SubmittedRequest();

        var result = request.Decide(request.Step(StepKind.Manager).Id, Manager, decision, comment, Later);

        Assert.Multiple(() =>
        {
            Assert.That(result.Error, Is.EqualTo(ApprovalErrors.CommentRequired));
            Assert.That(request.Status, Is.EqualTo(RequestStatus.InApproval), "nothing changed");
            Assert.That(request.Step(StepKind.Manager).Status, Is.EqualTo(StepStatus.Pending));
        });
    }

    [Test]
    public void Decide_CommentTooLong_IsValidationError()
    {
        var request = SubmittedRequest();

        var result = request.Decide(request.Step(StepKind.Manager).Id, Manager, Decision.Approve,
            new string('x', ApprovalStep.CommentMaxLength + 1), Later);

        Assert.That(result.Error, Is.EqualTo(ApprovalErrors.CommentTooLong));
    }

    [TestCase(RequestStatus.Draft)]
    [TestCase(RequestStatus.Approved)]
    [TestCase(RequestStatus.Rejected)]
    [TestCase(RequestStatus.ChangesRequested)]
    public void Decide_WhenNotInApproval_IsInvalidTransition(RequestStatus status)
    {
        var request = RequestIn(status);

        var result = request.Decide(1, Manager, Decision.Approve, null, Later);

        Assert.Multiple(() =>
        {
            Assert.That(result.Error?.Code, Is.EqualTo("request.invalid_transition"));
            Assert.That(result.Error?.Type, Is.EqualTo(ErrorType.Conflict));
        });
    }

    [Test]
    public void Decide_UnknownStep_IsNotFound()
    {
        var request = SubmittedRequest();

        Assert.That(request.Decide(12345, Manager, Decision.Approve, null, Later).Error, Is.EqualTo(ApprovalErrors.StepNotFound));
    }

    private static Request RequestIn(RequestStatus status)
    {
        if (status == RequestStatus.Draft)
        {
            return DraftRequest();
        }

        var request = SubmittedRequest();
        var stepId = request.Step(StepKind.Manager).Id;
        var outcome = status switch
        {
            RequestStatus.Approved => request.Decide(stepId, Manager, Decision.Approve, null, Now),
            RequestStatus.Rejected => request.Decide(stepId, Manager, Decision.Reject, "No", Now),
            RequestStatus.ChangesRequested => request.Decide(stepId, Manager, Decision.RequestChanges, "Fix it", Now),
            _ => throw new ArgumentOutOfRangeException(nameof(status)),
        };
        Assert.That(request.Status, Is.EqualTo(status), outcome.Error?.Code);
        return request;
    }
}
