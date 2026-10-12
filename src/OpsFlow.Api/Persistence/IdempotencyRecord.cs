namespace OpsFlow.Api.Persistence;

/// <summary>
/// A client-supplied Idempotency-Key and the response it produced (ADR-005). Infrastructure, not
/// domain: it records HTTP exchanges, not business facts.
/// </summary>
public sealed class IdempotencyRecord
{
    public const int KeyMaxLength = 100;

    private IdempotencyRecord() { } // EF Core

    public int UserId { get; private set; }

    public string Key { get; private set; } = null!;

    /// <summary>Method and path, e.g. <c>POST /api/v1/requests/42/submit</c>.</summary>
    public string Endpoint { get; private set; } = null!;

    /// <summary>SHA-256 of method, path, If-Match and body: the same key must mean the same command.</summary>
    public byte[] RequestHash { get; private set; } = [];

    /// <summary>Null while the command is in progress.</summary>
    public int? StatusCode { get; private set; }

    public string? ResponseContentType { get; private set; }

    public byte[]? ResponseBody { get; private set; }

    public string? ResponseETag { get; private set; }

    public string? ResponseLocation { get; private set; }

    public DateTimeOffset CreatedAt { get; private set; }

    public DateTimeOffset ExpiresAt { get; private set; }

    public bool IsCompleted => StatusCode is not null;

    public static IdempotencyRecord Reserve(int userId, string key, string endpoint, byte[] requestHash, DateTimeOffset now, TimeSpan retention) =>
        new()
        {
            UserId = userId,
            Key = key,
            Endpoint = endpoint,
            RequestHash = requestHash,
            CreatedAt = now,
            ExpiresAt = now + retention,
        };

    public void Complete(int statusCode, string? contentType, byte[] body, string? etag, string? location)
    {
        StatusCode = statusCode;
        ResponseContentType = contentType;
        ResponseBody = body;
        ResponseETag = etag;
        ResponseLocation = location;
    }
}
