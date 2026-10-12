namespace OpsFlow.Api.Infrastructure.Errors;

/// <summary>Error codes produced by the API layer (domain codes live with the domain errors).</summary>
public static class ErrorCodes
{
    public const string ValidationFailed = "validation.failed";
    public const string MalformedRequest = "request.malformed";
    public const string Unauthenticated = "auth.unauthenticated";
    public const string InvalidCredentials = "auth.invalid_credentials";
    public const string Forbidden = "auth.forbidden";
    public const string NotFound = "resource.not_found";
    public const string RequestNotFound = "request.not_found";
    public const string MethodNotAllowed = "http.method_not_allowed";
    public const string UnsupportedMediaType = "http.unsupported_media_type";
    public const string ETagMismatch = "concurrency.etag_mismatch";
    public const string IfMatchRequired = "concurrency.if_match_required";
    public const string RateLimitExceeded = "rate_limit.exceeded";
    public const string ServerError = "server.error";

    /// <summary>Code used when a response is produced without an explicit code (e.g. status code pages).</summary>
    public static string ForStatus(int statusCode) => statusCode switch
    {
        StatusCodes.Status400BadRequest => MalformedRequest,
        StatusCodes.Status401Unauthorized => Unauthenticated,
        StatusCodes.Status403Forbidden => Forbidden,
        StatusCodes.Status404NotFound => NotFound,
        StatusCodes.Status405MethodNotAllowed => MethodNotAllowed,
        StatusCodes.Status412PreconditionFailed => ETagMismatch,
        StatusCodes.Status415UnsupportedMediaType => UnsupportedMediaType,
        StatusCodes.Status429TooManyRequests => RateLimitExceeded,
        >= 500 => ServerError,
        _ => $"http.{statusCode}",
    };
}
