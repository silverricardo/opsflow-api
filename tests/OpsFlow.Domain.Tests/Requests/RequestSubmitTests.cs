using OpsFlow.Domain.Auditing;
using OpsFlow.Domain.Requests;
using static OpsFlow.Domain.Tests.Requests.TestData;

namespace OpsFlow.Domain.Tests.Requests;

[TestFixture]
public class RequestSubmitTests
{
    [Test]
    public void Submit_SmallAmount_CreatesOnlyManagerStepAssignedToManager()
    {
        var request = DraftRequest(1_200m);

        var result = request.Submit(Employee, ManagerId, DefaultPolicy(), Now.AddMinutes(5));

        Assert.That(result.IsSuccess, Is.True);
        Assert.Multiple(() =>
        {
            Assert.That(request.Status, Is.EqualTo(RequestStatus.InApproval));
            Assert.That(request.CurrentRound, Is.EqualTo(1));
            Assert.That(request.SubmittedAt, Is.EqualTo(Now.AddMinutes(5)));
            Assert.That(request.UpdatedAt, Is.EqualTo(Now.AddMinutes(5)));
            Assert.That(request.Steps, Has.Count.EqualTo(1));
        });
        var step = request.Steps[0];
        Assert.Multiple(() =>
        {
            Assert.That(step.Kind, Is.EqualTo(StepKind.Manager));
            Assert.That(step.AssigneeId, Is.EqualTo(ManagerId));
            Assert.That(step.Status, Is.EqualTo(StepStatus.Pending));
            Assert.That(step.Round, Is.EqualTo(1));
        });
    }

    [Test]
    public void Submit_LargeAmount_CreatesManagerFinanceDirectorInOrder_OnlyManagerAssigned()
    {
        var request = DraftRequest(75_000m);

        request.Submit(Employee, ManagerId, DefaultPolicy(), Now);

        Assert.Multiple(() =>
        {
            Assert.That(request.Steps.Select(s => s.Kind), Is.EqualTo(new[] { StepKind.Manager, StepKind.Finance, StepKind.Director }));
            Assert.That(request.Steps.Select(s => s.StepOrder), Is.EqualTo(new[] { 1, 2, 3 }));
            Assert.That(request.Steps.Select(s => s.AssigneeId), Is.EqualTo(new int?[] { ManagerId, null, null }));
        });
    }

    // BR-11: thresholds are strictly "greater than".
    [TestCase("5000", new[] { StepKind.Manager })]
    [TestCase("5000.01", new[] { StepKind.Manager, StepKind.Finance })]
    [TestCase("50000", new[] { StepKind.Manager, StepKind.Finance })]
    [TestCase("50000.01", new[] { StepKind.Manager, StepKind.Finance, StepKind.Director })]
    public void Submit_AtPolicyThresholds_GeneratesExpectedChain(decimal amount, StepKind[] expected)
    {
        var request = DraftRequest(amount);

        request.Submit(Employee, ManagerId, DefaultPolicy(), Now);

        Assert.That(request.Steps.Select(s => s.Kind), Is.EqualTo(expected));
    }

    [Test]
    public void Submit_ByNonOwner_IsForbiddenAndChangesNothing()
    {
        var request = DraftRequest();

        var result = request.Submit(OtherEmployee, ManagerId, DefaultPolicy(), Now);

        Assert.Multiple(() =>
        {
            Assert.That(result.Error, Is.EqualTo(RequestErrors.NotOwner));
            Assert.That(request.Status, Is.EqualTo(RequestStatus.Draft));
            Assert.That(request.Steps, Is.Empty);
        });
    }

    [Test]
    public void Submit_WhenRequesterHasNoManager_FailsAndChangesNothing()
    {
        var request = DraftRequest();

        var result = request.Submit(Employee, requesterManagerId: null, DefaultPolicy(), Now);

        Assert.Multiple(() =>
        {
            Assert.That(result.Error, Is.EqualTo(RequestErrors.RequesterHasNoManager));
            Assert.That(request.Status, Is.EqualTo(RequestStatus.Draft));
            Assert.That(request.CurrentRound, Is.Zero);
        });
    }

    [Test]
    public void Submit_WhenAlreadyInApproval_IsInvalidTransition()
    {
        var request = DraftRequest();
        request.Submit(Employee, ManagerId, DefaultPolicy(), Now);

        var result = request.Submit(Employee, ManagerId, DefaultPolicy(), Now);

        Assert.Multiple(() =>
        {
            Assert.That(result.Error?.Code, Is.EqualTo("request.invalid_transition"));
            Assert.That(request.Steps, Has.Count.EqualTo(1), "no duplicate steps");
        });
    }

    [Test]
    public void Submit_RecordsAuditEntryWithChain()
    {
        var request = DraftRequest(75_000m);

        request.Submit(Employee, ManagerId, DefaultPolicy(), Now);

        var entry = request.AuditEntries[^1];
        Assert.Multiple(() =>
        {
            Assert.That(entry.Action, Is.EqualTo(AuditActions.RequestSubmitted));
            Assert.That(entry.FromStatus, Is.EqualTo(RequestStatus.Draft));
            Assert.That(entry.ToStatus, Is.EqualTo(RequestStatus.InApproval));
            Assert.That(entry.Details, Does.Contain("\"round\":1").And.Contain("\"kind\":\"Director\""));
        });
    }
}
