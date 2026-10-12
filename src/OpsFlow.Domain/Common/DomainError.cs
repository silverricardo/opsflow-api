namespace OpsFlow.Domain.Common;

/// <summary>Category of a business-rule failure. The API maps each type to an HTTP status.</summary>
public enum ErrorType
{
    Validation,
    Forbidden,
    NotFound,
    Conflict,
    Unprocessable,
}

/// <summary>A business-rule failure with a stable, machine-readable code (e.g. <c>step.not_current</c>).</summary>
public sealed record DomainError(string Code, string Message, ErrorType Type);
