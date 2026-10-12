using OpsFlow.Domain.Requests;
using static OpsFlow.Domain.Tests.Requests.TestData;

namespace OpsFlow.Domain.Tests.Requests;

/// <summary>
/// IsCurrent is derived state used by the inbox query. Invariant: while in approval, exactly one step is
/// current and it is the first pending step of the current round; in any other status, none is.
/// </summary>
[TestFixture]
public class CurrentStepInvariantTests
{
    [Test]
    public void FullChain_CurrentStepAdvancesWithEachApproval_AndClearsWhenApproved()
    {
        var request = SubmittedRequest(75_000m);
        AssertInvariant(request, expectedCurrent: StepKind.Manager);

        request.Decide(request.Step(StepKind.Manager).Id, Manager, Decision.Approve, null, Now);
        AssertInvariant(request, expectedCurrent: StepKind.Finance);

        request.Decide(request.Step(StepKind.Finance).Id, Finance1, Decision.Approve, null, Now);
        AssertInvariant(request, expectedCurrent: StepKind.Director);

        request.Decide(request.Step(StepKind.Director).Id, Director, Decision.Approve, null, Now);
        AssertInvariant(request, expectedCurrent: null);
        Assert.That(request.Status, Is.EqualTo(RequestStatus.Approved));
    }

    [TestCase(Decision.Reject)]
    [TestCase(Decision.RequestChanges)]
    public void RejectOrRequestChanges_LeavesNoCurrentStep(Decision decision)
    {
        var request = SubmittedRequest(75_000m);

        request.Decide(request.Step(StepKind.Manager).Id, Manager, decision, "Reason", Now);

        AssertInvariant(request, expectedCurrent: null);
    }

    [Test]
    public void Cancel_LeavesNoCurrentStep()
    {
        var request = SubmittedRequest(75_000m);
        request.Decide(request.Step(StepKind.Manager).Id, Manager, Decision.Approve, null, Now);

        request.Cancel(Employee, Now);

        AssertInvariant(request, expectedCurrent: null);
    }

    [Test]
    public void Resubmission_MakesTheNewRoundsFirstStepCurrent_AndNoOldStep()
    {
        var request = SubmittedRequest(75_000m);
        request.Decide(request.Step(StepKind.Manager).Id, Manager, Decision.RequestChanges, "Split it", Now);
        request.Update(Employee, "Smaller", null, RequestCategory.Purchase, 3_000m, Now);

        request.Submit(Employee, Manager, DefaultPolicy(), Approvers, Now);
        AssignStepIds(request);

        AssertInvariant(request, expectedCurrent: StepKind.Manager);
        Assert.That(request.Steps.Where(s => s.Round == 1).Any(s => s.IsCurrent), Is.False);
    }

    [Test]
    public void ClaimAndReassign_DoNotMoveTheCurrentStep()
    {
        var request = SubmittedRequest(75_000m);
        request.ClaimStep(request.Step(StepKind.Finance).Id, Finance1, Now);
        request.ReassignStep(request.Step(StepKind.Manager).Id, Admin, OtherManager, true, Now);

        AssertInvariant(request, expectedCurrent: StepKind.Manager);
    }

    [Test]
    public void FailedOperations_DoNotChangeTheCurrentStep()
    {
        var request = SubmittedRequest(75_000m);

        request.Decide(request.Step(StepKind.Finance).Id, Finance1, Decision.Approve, null, Now);     // not current
        request.Decide(request.Step(StepKind.Manager).Id, Employee, Decision.Approve, null, Now);     // self-approval
        request.Decide(request.Step(StepKind.Manager).Id, Manager, Decision.Reject, null, Now);       // no comment

        AssertInvariant(request, expectedCurrent: StepKind.Manager);
    }

    private static void AssertInvariant(Request request, StepKind? expectedCurrent)
    {
        var current = request.Steps.Where(s => s.IsCurrent).ToList();
        if (expectedCurrent is null)
        {
            Assert.That(current, Is.Empty, "no step is current outside approval");
            return;
        }

        Assert.Multiple(() =>
        {
            Assert.That(request.Status, Is.EqualTo(RequestStatus.InApproval));
            Assert.That(current, Has.Count.EqualTo(1), "exactly one current step");
            Assert.That(current.Single().Kind, Is.EqualTo(expectedCurrent));
            Assert.That(current.Single(), Is.SameAs(request.CurrentRoundSteps().First(s => s.Status == StepStatus.Pending)));
        });
    }
}
