using OpsFlow.Domain.Requests;
using OpsFlow.Domain.Users;

namespace OpsFlow.Domain.Tests.Requests;

[TestFixture]
public class ApprovalStepEligibilityTests
{
    // Full matrix: every step kind × every role.
    [TestCase(StepKind.Manager, UserRole.Manager, true)]
    [TestCase(StepKind.Manager, UserRole.Director, true)]
    [TestCase(StepKind.Manager, UserRole.Finance, false)]
    [TestCase(StepKind.Manager, UserRole.Employee, false)]
    [TestCase(StepKind.Manager, UserRole.Admin, false)]
    [TestCase(StepKind.Finance, UserRole.Finance, true)]
    [TestCase(StepKind.Finance, UserRole.Manager, false)]
    [TestCase(StepKind.Finance, UserRole.Director, false)]
    [TestCase(StepKind.Finance, UserRole.Employee, false)]
    [TestCase(StepKind.Finance, UserRole.Admin, false)]
    [TestCase(StepKind.Director, UserRole.Director, true)]
    [TestCase(StepKind.Director, UserRole.Manager, false)]
    [TestCase(StepKind.Director, UserRole.Finance, false)]
    [TestCase(StepKind.Director, UserRole.Employee, false)]
    [TestCase(StepKind.Director, UserRole.Admin, false)]
    public void IsEligibleRole_MatchesPolicy(StepKind kind, UserRole role, bool expected) =>
        Assert.That(ApprovalStep.IsEligibleRole(kind, role), Is.EqualTo(expected));
}
