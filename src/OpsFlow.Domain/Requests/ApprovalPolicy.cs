namespace OpsFlow.Domain.Requests;

/// <summary>A step the policy requires for a given amount.</summary>
public readonly record struct StepTemplate(int StepOrder, StepKind Kind);

/// <summary>
/// Derives the approval chain for an amount (BR-11). The chain is copied onto the request at
/// submission, so later policy changes never affect requests already in approval (BR-10).
/// </summary>
public sealed class ApprovalPolicy
{
    private readonly IReadOnlyList<ApprovalPolicyRule> _rules;

    public ApprovalPolicy(IEnumerable<ApprovalPolicyRule> rules)
    {
        ArgumentNullException.ThrowIfNull(rules);
        _rules = [.. rules.OrderBy(r => r.StepOrder)];

        // A broken policy is a configuration bug, not a business-rule failure: fail loudly.
        if (_rules.Count == 0)
        {
            throw new InvalidOperationException("The approval policy has no rules.");
        }

        if (_rules[0].Kind != StepKind.Manager || _rules[0].AmountGreaterThan != 0)
        {
            throw new InvalidOperationException("The first approval step must be an unconditional Manager step.");
        }

        if (_rules.Select(r => r.StepOrder).Distinct().Count() != _rules.Count)
        {
            throw new InvalidOperationException("Approval policy step orders must be unique.");
        }
    }

    public IReadOnlyList<StepTemplate> StepsFor(decimal amount) =>
        [.. _rules.Where(r => amount > r.AmountGreaterThan).Select(r => new StepTemplate(r.StepOrder, r.Kind))];
}
