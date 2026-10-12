namespace OpsFlow.Domain.Requests;

public enum RequestStatus
{
    Draft,
    InApproval,
    ChangesRequested,
    Approved,
    Rejected,
    Cancelled,
    Fulfilled,
}

public enum RequestCategory
{
    Purchase,
    Expense,
}

/// <summary>The kind of approval step, which also determines who may decide it.</summary>
public enum StepKind
{
    Manager,
    Finance,
    Director,
}

public enum StepStatus
{
    Pending,
    Approved,
    Rejected,
    ChangesRequested,
    Skipped,
}

public enum Decision
{
    Approve,
    Reject,
    RequestChanges,
}
