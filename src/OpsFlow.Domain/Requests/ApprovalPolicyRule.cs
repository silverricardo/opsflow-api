namespace OpsFlow.Domain.Requests;

/// <summary>
/// One row of the approval policy: a step of <see cref="Kind"/> is required when the request amount
/// is strictly greater than <see cref="AmountGreaterThan"/>.
/// </summary>
public sealed class ApprovalPolicyRule
{
    private ApprovalPolicyRule() { } // EF Core

    public ApprovalPolicyRule(int id, int stepOrder, StepKind kind, decimal amountGreaterThan)
    {
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(stepOrder);
        ArgumentOutOfRangeException.ThrowIfNegative(amountGreaterThan);

        Id = id;
        StepOrder = stepOrder;
        Kind = kind;
        AmountGreaterThan = amountGreaterThan;
    }

    public int Id { get; private set; }

    public int StepOrder { get; private set; }

    public StepKind Kind { get; private set; }

    public decimal AmountGreaterThan { get; private set; }
}
