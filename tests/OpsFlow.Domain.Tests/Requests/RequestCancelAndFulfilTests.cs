using OpsFlow.Domain.Auditing;
using OpsFlow.Domain.Requests;
using static OpsFlow.Domain.Tests.Requests.TestData;

namespace OpsFlow.Domain.Tests.Requests;

[TestFixture]
public class RequestCancelAndFulfilTests
{
    [Test]
    public void Cancel_Draft_IsCancelled()
    {
        var request = DraftRequest();

        var result = request.Cancel(Employee, Now);

        Assert.Multiple(() =>
        {
            Assert.That(result.IsSuccess, Is.True);
            Assert.That(request.Status, Is.EqualTo(RequestStatus.Cancelled));
            Assert.That(request.CompletedAt, Is.EqualTo(Now));
            Assert.That(request.AuditEntries[^1].Action, Is.EqualTo(AuditActions.RequestCancelled));
        });
    }

    [Test]
    public void Cancel_InApproval_SkipsPendingSteps()
    {
        var request = SubmittedRequest(75_000m);
        request.Decide(request.Step(StepKind.Manager).Id, Manager, Decision.Approve, null, Now);

        request.Cancel(Employee, Now);

        Assert.That(request.Steps.Select(s => s.Status),
            Is.EqualTo(new[] { StepStatus.Approved, StepStatus.Skipped, StepStatus.Skipped }));
    }

    [Test]
    public void Cancel_ByNonOwner_IsForbidden() =>
        Assert.That(DraftRequest().Cancel(Manager, Now).Error, Is.EqualTo(RequestErrors.NotOwner));

    [Test]
    public void Cancel_Approved_IsInvalidTransition()
    {
        var request = ApprovedRequest();

        Assert.That(request.Cancel(Employee, Now).Error?.Code, Is.EqualTo("request.invalid_transition"));
    }

    [Test]
    public void Fulfil_ApprovedByFinance_IsFulfilled()
    {
        var request = ApprovedRequest();

        var result = request.Fulfil(Finance1, Now.AddDays(2));

        Assert.Multiple(() =>
        {
            Assert.That(result.IsSuccess, Is.True);
            Assert.That(request.Status, Is.EqualTo(RequestStatus.Fulfilled));
            Assert.That(request.CompletedAt, Is.EqualTo(Now.AddDays(2)));
            Assert.That(request.AuditEntries[^1].Action, Is.EqualTo(AuditActions.RequestFulfilled));
        });
    }

    [Test]
    public void Fulfil_ByNonFinance_IsForbidden() =>
        Assert.That(ApprovedRequest().Fulfil(Director, Now).Error, Is.EqualTo(RequestErrors.FulfilNotAllowed));

    [Test]
    public void Fulfil_OwnRequest_IsForbidden()
    {
        var request = SubmittedRequest(1_000m, requester: Finance1, managerId: Director.UserId);
        request.Decide(request.Step(StepKind.Manager).Id, Director, Decision.Approve, null, Now);

        Assert.Multiple(() =>
        {
            Assert.That(request.Fulfil(Finance1, Now).Error, Is.EqualTo(RequestErrors.SelfFulfilment));
            Assert.That(request.Fulfil(Finance2, Now).IsSuccess, Is.True);
        });
    }

    [Test]
    public void Fulfil_NotApproved_IsInvalidTransition() =>
        Assert.That(SubmittedRequest().Fulfil(Finance1, Now).Error?.Code, Is.EqualTo("request.invalid_transition"));

    private static Request ApprovedRequest()
    {
        var request = SubmittedRequest();
        request.Decide(request.Step(StepKind.Manager).Id, Manager, Decision.Approve, null, Now);
        Assert.That(request.Status, Is.EqualTo(RequestStatus.Approved));
        return request;
    }
}
