namespace OpsFlow.Domain.Auditing;

public static class AuditActions
{
    public const string RequestCreated = "RequestCreated";
    public const string RequestUpdated = "RequestUpdated";
    public const string RequestSubmitted = "RequestSubmitted";
    public const string StepApproved = "StepApproved";
    public const string StepRejected = "StepRejected";
    public const string ChangesRequested = "ChangesRequested";
    public const string StepClaimed = "StepClaimed";
    public const string StepReassigned = "StepReassigned";
    public const string RequestCancelled = "RequestCancelled";
    public const string RequestFulfilled = "RequestFulfilled";
}
