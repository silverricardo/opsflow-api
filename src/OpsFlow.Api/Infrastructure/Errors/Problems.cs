using Microsoft.AspNetCore.Http.HttpResults;
using OpsFlow.Domain.Common;

namespace OpsFlow.Api.Infrastructure.Errors;

/// <summary>Factory for RFC 9457 problem responses that always carry a stable <c>code</c>.</summary>
public static class Problems
{
    public static ProblemHttpResult Create(int statusCode, string code, string title, string? detail = null) =>
        TypedResults.Problem(
            statusCode: statusCode,
            title: title,
            detail: detail,
            extensions: new Dictionary<string, object?> { ["code"] = code });

    public static ProblemHttpResult ToProblem(this DomainError error)
    {
        ArgumentNullException.ThrowIfNull(error);

        var (status, title) = error.Type switch
        {
            ErrorType.Validation => (StatusCodes.Status400BadRequest, "Validation failed"),
            ErrorType.Forbidden => (StatusCodes.Status403Forbidden, "Forbidden"),
            ErrorType.NotFound => (StatusCodes.Status404NotFound, "Not found"),
            ErrorType.Conflict => (StatusCodes.Status409Conflict, "Conflict"),
            ErrorType.Unprocessable => (StatusCodes.Status422UnprocessableEntity, "Unprocessable request"),
            _ => throw new ArgumentOutOfRangeException(nameof(error), error.Type, "Unknown error type."),
        };

        return Create(status, error.Code, title, error.Message);
    }

    public static ProblemHttpResult RequestNotFound() =>
        Create(StatusCodes.Status404NotFound, ErrorCodes.RequestNotFound, "Not found", "The request does not exist.");

    public static ProblemHttpResult IfMatchRequired() =>
        Create(
            StatusCodes.Status428PreconditionRequired,
            ErrorCodes.IfMatchRequired,
            "Precondition required",
            "Send the request's current ETag in the If-Match header.");

    public static ProblemHttpResult ETagMismatch() =>
        Create(
            StatusCodes.Status412PreconditionFailed,
            ErrorCodes.ETagMismatch,
            "Precondition failed",
            "The request was modified by someone else. Reload it and try again.");
}
