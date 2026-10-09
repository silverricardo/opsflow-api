namespace OpsFlow.Domain.Auditing;

public static class AuditActions
{
    public const string RequestCreated = "RequestCreated";
    public const string RequestUpdated = "RequestUpdated";
    public const string RequestSubmitted = "RequestSubmitted";
    public const string StepApproved = "StepApproved";
    public const string StepRejected = "StepRejected";
    public const string ChangesRequested = "ChangesRequested";
}
