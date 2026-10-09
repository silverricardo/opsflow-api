using OpsFlow.Domain.Auditing;
using OpsFlow.Domain.Requests;
using static OpsFlow.Domain.Tests.Requests.TestData;

namespace OpsFlow.Domain.Tests.Requests;

[TestFixture]
public class RequestUpdateTests
{
    [Test]
    public void Update_Draft_ChangesDetailsAndRecordsBeforeAndAfter()
    {
        var request = DraftRequest(1_200m);

        var result = request.Update(Employee, " Better laptop ", "Needs 32 GB", RequestCategory.Purchase, 1_800m, Now.AddDays(1));

        Assert.That(result.IsSuccess, Is.True);
        Assert.Multiple(() =>
        {
            Assert.That(request.Title, Is.EqualTo("Better laptop"));
            Assert.That(request.Amount, Is.EqualTo(1_800m));
            Assert.That(request.Status, Is.EqualTo(RequestStatus.Draft));
            Assert.That(request.UpdatedAt, Is.EqualTo(Now.AddDays(1)));
            Assert.That(request.AuditEntries[^1].Action, Is.EqualTo(AuditActions.RequestUpdated));
            Assert.That(request.AuditEntries[^1].Details, Does.Contain("\"before\"").And.Contain("1200").And.Contain("1800"));
        });
    }

    [Test]
    public void Update_ByNonOwner_IsForbidden() =>
        Assert.That(DraftRequest().Update(Manager, "Valid", null, RequestCategory.Expense, 10m, Now).Error,
            Is.EqualTo(RequestErrors.NotOwner));

    [Test]
    public void Update_InApproval_IsInvalidTransition()
    {
        var request = SubmittedRequest();

        var result = request.Update(Employee, "Valid", null, RequestCategory.Expense, 10m, Now);

        Assert.Multiple(() =>
        {
            Assert.That(result.Error?.Code, Is.EqualTo("request.invalid_transition"));
            Assert.That(request.Title, Is.EqualTo("New laptop"));
        });
    }

    [Test]
    public void Update_WithInvalidAmount_FailsAndChangesNothing()
    {
        var request = DraftRequest(1_200m);

        var result = request.Update(Employee, "Valid", null, RequestCategory.Expense, 0m, Now);

        Assert.Multiple(() =>
        {
            Assert.That(result.Error, Is.EqualTo(RequestErrors.InvalidAmount));
            Assert.That(request.Amount, Is.EqualTo(1_200m));
        });
    }
}
