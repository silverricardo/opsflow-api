using OpsFlow.Domain.Users;

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

    /// <summary>Who may decide an unassigned step of this kind (BR-23, BR-25).</summary>
    public static bool IsEligibleRole(StepKind kind, UserRole role) => kind switch
    {
        StepKind.Manager => role is UserRole.Manager or UserRole.Director,
        StepKind.Finance => role == UserRole.Finance,
        StepKind.Director => role == UserRole.Director,
        _ => false,
    };

    /// <summary>Deciding an unassigned step implicitly assigns it to the decider (BR-25).</summary>
    internal void RecordDecision(int deciderId, StepStatus outcome, string? comment, DateTimeOffset now)
    {
        AssigneeId ??= deciderId;
        DecidedById = deciderId;
        DecidedAt = now;
        Status = outcome;
        Comment = comment;
    }

    internal void Skip() => Status = StepStatus.Skipped;

    internal void AssignTo(int userId) => AssigneeId = userId;
}
