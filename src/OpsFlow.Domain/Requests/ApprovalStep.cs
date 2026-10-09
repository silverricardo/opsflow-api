namespace OpsFlow.Domain.Requests;

/// <summary>One step of a request's approval chain. Owned by <see cref="Request"/>; modified only through it.</summary>
public sealed class ApprovalStep
{
    public const int CommentMaxLength = 1000;

    private ApprovalStep() { } // EF Core

    internal ApprovalStep(int round, int stepOrder, StepKind kind, int? assigneeId)
    {
        Round = round;
        StepOrder = stepOrder;
        Kind = kind;
        AssigneeId = assigneeId;
        Status = StepStatus.Pending;
    }

    public long Id { get; private set; }

    public long RequestId { get; private set; }

    public int Round { get; private set; }

    public int StepOrder { get; private set; }

    public StepKind Kind { get; private set; }

    public StepStatus Status { get; private set; }

    public int? AssigneeId { get; private set; }

    public int? DecidedById { get; private set; }

    public DateTimeOffset? DecidedAt { get; private set; }

    public string? Comment { get; private set; }
}
