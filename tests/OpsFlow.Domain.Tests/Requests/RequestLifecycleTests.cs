using OpsFlow.Domain.Requests;
using static OpsFlow.Domain.Tests.Requests.TestData;

namespace OpsFlow.Domain.Tests.Requests;

/// <summary>
/// The lifecycle diagram in docs/requirements.md as a table (BR-05): every operation from every
/// status is either the documented transition or rejected as an invalid transition.
/// </summary>
[TestFixture]
public class RequestLifecycleTests
{
    public enum Operation { Update, Submit, Cancel, Fulfil }

    private static readonly RequestStatus[] AllStatuses = Enum.GetValues<RequestStatus>();

    // (from, operation) → resulting status; anything absent must be rejected.
    private static readonly Dictionary<(RequestStatus, Operation), RequestStatus> Allowed = new()
    {
        [(RequestStatus.Draft, Operation.Update)] = RequestStatus.Draft,
        [(RequestStatus.Draft, Operation.Submit)] = RequestStatus.InApproval,
        [(RequestStatus.Draft, Operation.Cancel)] = RequestStatus.Cancelled,
        [(RequestStatus.InApproval, Operation.Cancel)] = RequestStatus.Cancelled,
        [(RequestStatus.ChangesRequested, Operation.Update)] = RequestStatus.ChangesRequested,
        [(RequestStatus.ChangesRequested, Operation.Submit)] = RequestStatus.InApproval,
        [(RequestStatus.ChangesRequested, Operation.Cancel)] = RequestStatus.Cancelled,
        [(RequestStatus.Approved, Operation.Fulfil)] = RequestStatus.Fulfilled,
    };

    private static IEnumerable<TestCaseData> AllCombinations() =>
        from status in AllStatuses
        from operation in Enum.GetValues<Operation>()
        select new TestCaseData(status, operation).SetName($"{status} --{operation}-->");

    [TestCaseSource(nameof(AllCombinations))]
    public void Operation_FromStatus_FollowsLifecycle(RequestStatus from, Operation operation)
    {
        var request = RequestIn(from);

        var result = operation switch
        {
            Operation.Update => request.Update(Employee, "Edited title", null, RequestCategory.Purchase, 10m, Now),
            Operation.Submit => request.Submit(Employee, ManagerId, DefaultPolicy(), Now),
            Operation.Cancel => request.Cancel(Employee, Now),
            Operation.Fulfil => request.Fulfil(Finance1, Now),
            _ => throw new ArgumentOutOfRangeException(nameof(operation)),
        };

        if (Allowed.TryGetValue((from, operation), out var expected))
        {
            Assert.Multiple(() =>
            {
                Assert.That(result.IsSuccess, Is.True, result.Error?.Code);
                Assert.That(request.Status, Is.EqualTo(expected));
            });
        }
        else
        {
            Assert.Multiple(() =>
            {
                Assert.That(result.Error?.Code, Is.EqualTo("request.invalid_transition"));
                Assert.That(request.Status, Is.EqualTo(from), "state unchanged");
            });
        }
    }

    private static Request RequestIn(RequestStatus status)
    {
        if (status == RequestStatus.Draft)
        {
            return DraftRequest();
        }

        var request = SubmittedRequest();
        var stepId = request.Step(StepKind.Manager).Id;
        switch (status)
        {
            case RequestStatus.InApproval:
                break;
            case RequestStatus.Approved:
                request.Decide(stepId, Manager, Decision.Approve, null, Now);
                break;
            case RequestStatus.Rejected:
                request.Decide(stepId, Manager, Decision.Reject, "No", Now);
                break;
            case RequestStatus.ChangesRequested:
                request.Decide(stepId, Manager, Decision.RequestChanges, "Fix", Now);
                break;
            case RequestStatus.Cancelled:
                request.Cancel(Employee, Now);
                break;
            case RequestStatus.Fulfilled:
                request.Decide(stepId, Manager, Decision.Approve, null, Now);
                request.Fulfil(Finance1, Now);
                break;
        }

        Assert.That(request.Status, Is.EqualTo(status), "arrange");
        return request;
    }
}
