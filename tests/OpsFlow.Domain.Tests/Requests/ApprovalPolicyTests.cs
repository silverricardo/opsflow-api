using OpsFlow.Domain.Requests;

namespace OpsFlow.Domain.Tests.Requests;

[TestFixture]
public class ApprovalPolicyTests
{
    [Test]
    public void StepsFor_ReturnsStepsOrderedByStepOrder_RegardlessOfRuleOrder()
    {
        var policy = new ApprovalPolicy(
        [
            new ApprovalPolicyRule(3, 3, StepKind.Director, 50_000m),
            new ApprovalPolicyRule(1, 1, StepKind.Manager, 0m),
            new ApprovalPolicyRule(2, 2, StepKind.Finance, 5_000m),
        ]);

        Assert.That(policy.StepsFor(60_000m).Select(s => s.Kind),
            Is.EqualTo(new[] { StepKind.Manager, StepKind.Finance, StepKind.Director }));
    }

    [Test]
    public void Constructor_WithNoRules_Throws() =>
        Assert.That(() => new ApprovalPolicy([]), Throws.InvalidOperationException);

    [Test]
    public void Constructor_WhenFirstStepIsNotUnconditionalManager_Throws() =>
        Assert.That(
            () => new ApprovalPolicy([new ApprovalPolicyRule(1, 1, StepKind.Finance, 0m)]),
            Throws.InvalidOperationException);

    [Test]
    public void Constructor_WithDuplicateStepOrder_Throws() =>
        Assert.That(
            () => new ApprovalPolicy(
            [
                new ApprovalPolicyRule(1, 1, StepKind.Manager, 0m),
                new ApprovalPolicyRule(2, 1, StepKind.Finance, 5_000m),
            ]),
            Throws.InvalidOperationException);
}
