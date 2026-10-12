using OpsFlow.Domain.Auditing;
using OpsFlow.Domain.Requests;
using OpsFlow.Domain.Users;
using static OpsFlow.Domain.Tests.Requests.TestData;

namespace OpsFlow.Domain.Tests.Requests;

[TestFixture]
public class RequestAssignmentTests
{
    private static readonly Actor Finance3 = new(22, UserRole.Finance);

    [Test]
    public void Claim_UnassignedPoolStep_AssignsItToCaller()
    {
        var request = SubmittedRequest(75_000m);

        var result = request.ClaimStep(request.Step(StepKind.Finance).Id, Finance1, Now);

        Assert.Multiple(() =>
        {
            Assert.That(result.IsSuccess, Is.True);
            Assert.That(request.Step(StepKind.Finance).AssigneeId, Is.EqualTo(Finance1.UserId));
            Assert.That(request.AuditEntries[^1].Action, Is.EqualTo(AuditActions.StepClaimed));
        });
    }

    [Test]
    public void Claim_AlreadyAssignedStep_IsConflict()
    {
        var request = SubmittedRequest(75_000m);
        request.ClaimStep(request.Step(StepKind.Finance).Id, Finance1, Now);

        Assert.Multiple(() =>
        {
            Assert.That(request.ClaimStep(request.Step(StepKind.Finance).Id, Finance2, Now).Error, Is.EqualTo(ApprovalErrors.StepAlreadyAssigned));
            Assert.That(request.ClaimStep(request.Step(StepKind.Manager).Id, OtherManager, Now).Error, Is.EqualTo(ApprovalErrors.StepAlreadyAssigned));
        });
    }

    [Test]
    public void Claim_ByWrongRole_IsNotEligible()
    {
        var request = SubmittedRequest(75_000m);

        Assert.That(request.ClaimStep(request.Step(StepKind.Finance).Id, Director, Now).Error, Is.EqualTo(ApprovalErrors.NotEligible));
    }

    [Test]
    public void Claim_OwnRequest_IsSelfApproval()
    {
        var request = SubmittedRequest(7_000m, requester: Finance1, manager: Director);

        Assert.That(request.ClaimStep(request.Step(StepKind.Finance).Id, Finance1, Now).Error, Is.EqualTo(ApprovalErrors.SelfApproval));
    }

    [Test]
    public void Claim_ByUserWhoAlreadyDecidedThisRound_IsDuplicateApprover()
    {
        var request = SubmittedRequest(60_000m, requester: Manager, manager: Director);
        request.Decide(request.Step(StepKind.Manager).Id, Director, Decision.Approve, null, Now);

        Assert.That(request.ClaimStep(request.Step(StepKind.Director).Id, Director, Now).Error, Is.EqualTo(ApprovalErrors.DuplicateApprover));
    }

    [Test]
    public void Claim_DecidedStep_IsAlreadyDecided()
    {
        var request = SubmittedRequest(75_000m);
        request.Decide(request.Step(StepKind.Manager).Id, Manager, Decision.Approve, null, Now);
        request.Decide(request.Step(StepKind.Finance).Id, Finance1, Decision.Approve, null, Now);

        Assert.That(request.ClaimStep(request.Step(StepKind.Finance).Id, Finance2, Now).Error, Is.EqualTo(ApprovalErrors.StepAlreadyDecided));
    }

    [Test]
    public void Reassign_ByAdminToEligibleUser_MovesStepAndAudits()
    {
        var request = SubmittedRequest();

        var result = request.ReassignStep(request.Step(StepKind.Manager).Id, Admin, OtherManager, newAssigneeIsActive: true, Now);

        Assert.Multiple(() =>
        {
            Assert.That(result.IsSuccess, Is.True);
            Assert.That(request.Step(StepKind.Manager).AssigneeId, Is.EqualTo(OtherManager.UserId));
            Assert.That(request.AuditEntries[^1].Action, Is.EqualTo(AuditActions.StepReassigned));
            Assert.That(request.AuditEntries[^1].Details, Does.Contain($"\"fromAssigneeId\":{ManagerId}"));
        });

        // BR-25: the previous assignee can no longer decide it.
        Assert.That(request.Decide(request.Step(StepKind.Manager).Id, Manager, Decision.Approve, null, Now).Error,
            Is.EqualTo(ApprovalErrors.NotAssignee));
    }

    [Test]
    public void Reassign_ManagerStepToDirector_IsAllowed() =>
        Assert.That(SubmittedRequest().ReassignStep(1, Admin, Director2, true, Now).IsSuccess, Is.True);

    [Test]
    public void Reassign_ByNonAdmin_IsForbidden()
    {
        var request = SubmittedRequest();

        Assert.That(request.ReassignStep(request.Step(StepKind.Manager).Id, Director, OtherManager, true, Now).Error,
            Is.EqualTo(ApprovalErrors.ReassignNotAllowed));
    }

    [TestCase(false, UserRole.Manager, 11, TestName = "inactive user")]
    [TestCase(true, UserRole.Finance, 22, TestName = "wrong role")]
    [TestCase(true, UserRole.Employee, 1, TestName = "the requester")]
    public void Reassign_ToIneligibleUser_IsUnprocessable(bool active, UserRole role, int userId)
    {
        var request = SubmittedRequest();

        var result = request.ReassignStep(request.Step(StepKind.Manager).Id, Admin, new Actor(userId, role), active, Now);

        Assert.Multiple(() =>
        {
            Assert.That(result.Error, Is.EqualTo(ApprovalErrors.AssigneeNotEligible));
            Assert.That(request.Step(StepKind.Manager).AssigneeId, Is.EqualTo(ManagerId), "unchanged");
        });
    }

    [Test]
    public void Reassign_ToUserWhoDecidedEarlierStep_IsUnprocessable()
    {
        var request = SubmittedRequest(75_000m);
        request.Decide(request.Step(StepKind.Manager).Id, Manager, Decision.Approve, null, Now);
        request.Decide(request.Step(StepKind.Finance).Id, Finance1, Decision.Approve, null, Now);

        // Finance3 is fine; but the Director step cannot go to someone who already decided this round.
        Assert.Multiple(() =>
        {
            Assert.That(request.ReassignStep(request.Step(StepKind.Director).Id, Admin, new Actor(ManagerId, UserRole.Director), true, Now).Error,
                Is.EqualTo(ApprovalErrors.AssigneeNotEligible));
            Assert.That(request.ReassignStep(request.Step(StepKind.Director).Id, Admin, Director2, true, Now).IsSuccess, Is.True);
            Assert.That(Finance3.Role, Is.EqualTo(UserRole.Finance));
        });
    }
}
