using OpsFlow.Domain.Requests;

namespace OpsFlow.Domain.Auditing;

/// <summary>
/// An append-only record of a business action on a request. Created by the aggregate and persisted
/// in the same transaction as the change it describes (BR-50, BR-51).
/// </summary>
public sealed class AuditEntry
{
    public const int ActionMaxLength = 64;

    private AuditEntry() { } // EF Core

    internal AuditEntry(
        int actorId,
        string action,
        RequestStatus? fromStatus,
        RequestStatus? toStatus,
        string? details,
        DateTimeOffset occurredAt)
    {
        ActorId = actorId;
        Action = action;
        FromStatus = fromStatus;
        ToStatus = toStatus;
        Details = details;
        OccurredAt = occurredAt;
    }

    public long Id { get; private set; }

    public long RequestId { get; private set; }

    public int ActorId { get; private set; }

    public string Action { get; private set; } = null!;

    public RequestStatus? FromStatus { get; private set; }

    public RequestStatus? ToStatus { get; private set; }

    /// <summary>Action-specific details as a JSON object.</summary>
    public string? Details { get; private set; }

    public DateTimeOffset OccurredAt { get; private set; }
}
