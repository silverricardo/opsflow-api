using OpsFlow.Domain.Requests;

namespace OpsFlow.Api.Features.Requests;

public sealed record UserSummary(int Id, string DisplayName);

public sealed record ApprovalStepResponse(
    long Id,
    int Round,
    int Order,
    StepKind Kind,
    StepStatus Status,
    UserSummary? Assignee,
    UserSummary? DecidedBy,
    DateTimeOffset? DecidedAt,
    string? Comment);

public sealed record RequestDetailsResponse(
    long Id,
    string Reference,
    string Title,
    string? Description,
    RequestCategory Category,
    decimal Amount,
    RequestStatus Status,
    UserSummary Requester,
    int CurrentRound,
    DateTimeOffset CreatedAt,
    DateTimeOffset UpdatedAt,
    DateTimeOffset? SubmittedAt,
    DateTimeOffset? CompletedAt,
    IReadOnlyList<ApprovalStepResponse> Steps);
