using OpsFlow.Domain.Auditing;
using OpsFlow.Domain.Common;
using OpsFlow.Domain.Requests;
using static OpsFlow.Domain.Tests.Requests.TestData;

namespace OpsFlow.Domain.Tests.Requests;

[TestFixture]
public class RequestCreateTests
{
    [Test]
    public void Create_WithValidDetails_IsDraftOwnedByRequester()
    {
        var result = Request.Create(Employee, "  New laptop  ", "  ", RequestCategory.Purchase, 1_200.50m, Now);

        Assert.That(result.IsSuccess, Is.True);
        var request = result.Value;
        Assert.Multiple(() =>
        {
            Assert.That(request.Status, Is.EqualTo(RequestStatus.Draft));
            Assert.That(request.RequesterId, Is.EqualTo(Employee.UserId));
            Assert.That(request.Title, Is.EqualTo("New laptop"), "title is trimmed");
            Assert.That(request.Description, Is.Null, "blank description is stored as null");
            Assert.That(request.CurrentRound, Is.Zero);
            Assert.That(request.Steps, Is.Empty);
            Assert.That(request.CreatedAt, Is.EqualTo(Now));
        });
    }

    [Test]
    public void Create_RecordsAuditEntry()
    {
        var request = DraftRequest();

        Assert.That(request.AuditEntries, Has.Count.EqualTo(1));
        var entry = request.AuditEntries[0];
        Assert.Multiple(() =>
        {
            Assert.That(entry.Action, Is.EqualTo(AuditActions.RequestCreated));
            Assert.That(entry.ActorId, Is.EqualTo(Employee.UserId));
            Assert.That(entry.FromStatus, Is.Null);
            Assert.That(entry.ToStatus, Is.EqualTo(RequestStatus.Draft));
            Assert.That(entry.Details, Does.Contain("\"category\":\"Purchase\""));
        });
    }

    [Test]
    public void Create_ByAdmin_IsForbidden()
    {
        var result = Request.Create(Admin, "New laptop", null, RequestCategory.Purchase, 100m, Now);

        Assert.That(result.Error, Is.EqualTo(RequestErrors.AdminCannotCreate));
    }

    [TestCase("ab")]
    [TestCase("   ab   ")]
    [TestCase("")]
    public void Create_WithTitleTooShort_FailsValidation(string title) =>
        AssertValidationError(Request.Create(Employee, title, null, RequestCategory.Expense, 10m, Now), "request.invalid_title");

    [Test]
    public void Create_WithTitleTooLong_FailsValidation() =>
        AssertValidationError(
            Request.Create(Employee, new string('x', Request.TitleMaxLength + 1), null, RequestCategory.Expense, 10m, Now),
            "request.invalid_title");

    [Test]
    public void Create_WithDescriptionTooLong_FailsValidation() =>
        AssertValidationError(
            Request.Create(Employee, "Valid", new string('x', Request.DescriptionMaxLength + 1), RequestCategory.Expense, 10m, Now),
            "request.invalid_description");

    [TestCase("0")]
    [TestCase("-1")]
    [TestCase("1000000.01")]
    [TestCase("10.005")]
    public void Create_WithInvalidAmount_FailsValidation(decimal amount) =>
        AssertValidationError(Request.Create(Employee, "Valid", null, RequestCategory.Expense, amount, Now), "request.invalid_amount");

    [TestCase("0.01")]
    [TestCase("1000000")]
    public void Create_WithBoundaryAmount_Succeeds(decimal amount) =>
        Assert.That(Request.Create(Employee, "Valid", null, RequestCategory.Expense, amount, Now).IsSuccess, Is.True);

    private static void AssertValidationError(Result result, string expectedCode)
    {
        Assert.That(result.IsSuccess, Is.False);
        Assert.Multiple(() =>
        {
            Assert.That(result.Error!.Code, Is.EqualTo(expectedCode));
            Assert.That(result.Error.Type, Is.EqualTo(ErrorType.Validation));
        });
    }
}
