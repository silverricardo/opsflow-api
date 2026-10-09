using OpsFlow.Domain.Requests;
using OpsFlow.Domain.Users;

namespace OpsFlow.Domain.Tests.Requests;

internal static class TestData
{
    public const int ManagerId = 10;

    public static readonly DateTimeOffset Now = new(2026, 10, 9, 12, 0, 0, TimeSpan.Zero);

    public static readonly Actor Employee = new(1, UserRole.Employee);
    public static readonly Actor OtherEmployee = new(2, UserRole.Employee);
    public static readonly Actor Admin = new(99, UserRole.Admin);
    public static readonly Actor Manager = new(ManagerId, UserRole.Manager);
    public static readonly Actor OtherManager = new(11, UserRole.Manager);
    public static readonly Actor Finance1 = new(20, UserRole.Finance);
    public static readonly Actor Finance2 = new(21, UserRole.Finance);
    public static readonly Actor Director = new(30, UserRole.Director);

    /// <summary>The default policy from BR-11.</summary>
    public static ApprovalPolicy DefaultPolicy() => new(
    [
        new ApprovalPolicyRule(1, 1, StepKind.Manager, 0m),
        new ApprovalPolicyRule(2, 2, StepKind.Finance, 5_000m),
        new ApprovalPolicyRule(3, 3, StepKind.Director, 50_000m),
    ]);

    public static Request DraftRequest(decimal amount = 1_200m, Actor? requester = null) =>
        Request.Create(requester ?? Employee, "New laptop", "For the new hire", RequestCategory.Purchase, amount, Now).Value;

    /// <summary>A request in approval whose steps have IDs 1..n, as they would after being saved.</summary>
    public static Request SubmittedRequest(decimal amount = 1_200m, Actor? requester = null, int managerId = ManagerId)
    {
        var actor = requester ?? Employee;
        var request = DraftRequest(amount, actor);
        Assert.That(request.Submit(actor, managerId, DefaultPolicy(), Now).IsSuccess, Is.True);
        AssignStepIds(request);
        return request;
    }

    public static void AssignStepIds(Request request)
    {
        var idProperty = typeof(ApprovalStep).GetProperty(nameof(ApprovalStep.Id))!;
        var nextId = request.Steps.Max(s => s.Id) + 1;
        foreach (var step in request.Steps.Where(s => s.Id == 0))
        {
            idProperty.SetValue(step, nextId++);
        }
    }

    public static ApprovalStep Step(this Request request, StepKind kind) =>
        request.CurrentRoundSteps().Single(s => s.Kind == kind);
}
