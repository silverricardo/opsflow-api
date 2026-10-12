using OpsFlow.Domain.Common;

namespace OpsFlow.Domain.Requests;

public static class ApprovalErrors
{
    public static readonly DomainError StepNotFound = new(
        "step.not_found", "The approval step does not exist in the request's current round.", ErrorType.NotFound);

    public static readonly DomainError StepAlreadyDecided = new(
        "step.already_decided", "The approval step has already been decided.", ErrorType.Conflict);

    public static readonly DomainError StepNotCurrent = new(
        "step.not_current", "Earlier approval steps must be decided first.", ErrorType.Conflict);

    public static readonly DomainError SelfApproval = new(
        "approval.self_approval", "You cannot decide an approval step on your own request.", ErrorType.Forbidden);

    public static readonly DomainError DuplicateApprover = new(
        "approval.duplicate_approver",
        "You have already decided another step of this approval round.",
        ErrorType.Forbidden);

    public static readonly DomainError NotAssignee = new(
        "step.not_assignee", "The approval step is assigned to someone else.", ErrorType.Forbidden);

    public static readonly DomainError NotEligible = new(
        "step.not_eligible", "Your role cannot decide this kind of approval step.", ErrorType.Forbidden);

    public static readonly DomainError CommentRequired = new(
        "step.comment_required", "A comment is required when rejecting or requesting changes.", ErrorType.Validation);

    public static readonly DomainError CommentTooLong = new(
        "step.comment_too_long",
        $"The comment must be at most {ApprovalStep.CommentMaxLength} characters.",
        ErrorType.Validation);

    public static readonly DomainError StepAlreadyAssigned = new(
        "step.already_assigned", "The approval step is already assigned.", ErrorType.Conflict);

    public static readonly DomainError ReassignNotAllowed = new(
        "step.reassign_not_allowed", "Only administrators can reassign approval steps.", ErrorType.Forbidden);

    public static readonly DomainError AssigneeNotEligible = new(
        "user.not_eligible",
        "The user cannot be assigned this step: they must be active, hold an eligible role, not be the requester, " +
        "and not have decided another step of this round.",
        ErrorType.Unprocessable);
}
