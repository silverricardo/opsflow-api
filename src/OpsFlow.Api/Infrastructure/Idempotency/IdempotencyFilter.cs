using System.Security.Cryptography;
using System.Text;
using System.Text.RegularExpressions;
using Microsoft.Data.SqlClient;
using Microsoft.EntityFrameworkCore;
using OpsFlow.Api.Infrastructure.Auth;
using OpsFlow.Api.Infrastructure.Errors;
using OpsFlow.Api.Persistence;

namespace OpsFlow.Api.Infrastructure.Idempotency;

/// <summary>
/// Makes POST commands safe to retry (BR-61, ADR-005). With an <c>Idempotency-Key</c> header:
/// a first request reserves the key, runs, and stores its exact response; a retry with the same key and
/// payload gets that response replayed (no second effect); the same key with a different payload is
/// rejected; a retry while the first is still running gets 409. Server errors release the key so the
/// client can retry. Without the header, requests behave normally.
/// </summary>
public sealed partial class IdempotencyFilter(IServiceScopeFactory scopes, TimeProvider clock) : IEndpointFilter
{
    public const string HeaderName = "Idempotency-Key";
    public const string ReplayedHeader = "Idempotent-Replayed";
    public static readonly TimeSpan Retention = TimeSpan.FromHours(24);

    public async ValueTask<object?> InvokeAsync(EndpointFilterInvocationContext context, EndpointFilterDelegate next)
    {
        ArgumentNullException.ThrowIfNull(context);
        ArgumentNullException.ThrowIfNull(next);

        var http = context.HttpContext;
        var key = http.Request.Headers[HeaderName].ToString();
        if (key.Length == 0)
        {
            return await next(context);
        }

        if (!ValidKey().IsMatch(key))
        {
            return Problems.Create(
                StatusCodes.Status400BadRequest,
                "idempotency.invalid_key",
                "Invalid Idempotency-Key",
                $"Use 1–{IdempotencyRecord.KeyMaxLength} characters from A–Z, a–z, 0–9, '-', '_', '.', ':'.");
        }

        var userId = http.User.FindUserId()
            ?? throw new InvalidOperationException("Idempotency requires an authenticated endpoint.");
        var endpoint = $"{http.Request.Method} {http.Request.Path}";
        var hash = await ComputeRequestHashAsync(http.Request, http.RequestAborted);

        var (existing, problem) = await ReserveAsync(userId, key, endpoint, hash, http.RequestAborted);
        if (problem is not null)
        {
            return problem;
        }

        if (existing is not null)
        {
            return new ReplayedResult(existing);
        }

        IResult result;
        try
        {
            result = await next(context) as IResult
                ?? throw new InvalidOperationException("Idempotent endpoints must return an IResult.");
        }
        catch
        {
            await ReleaseAsync(userId, key);
            throw;
        }

        // Execute the result into a buffer so the exact response can be stored, then send it.
        var originalBody = http.Response.Body;
        byte[] body;
        await using (var buffer = new MemoryStream())
        {
            http.Response.Body = buffer;
            try
            {
                await result.ExecuteAsync(http);
            }
            catch
            {
                http.Response.Body = originalBody;
                await ReleaseAsync(userId, key);
                throw;
            }

            http.Response.Body = originalBody;
            body = buffer.ToArray();
        }

        if (http.Response.StatusCode >= StatusCodes.Status500InternalServerError)
        {
            await ReleaseAsync(userId, key);
        }
        else
        {
            await CompleteAsync(userId, key, http.Response, body);
        }

        await originalBody.WriteAsync(body, http.RequestAborted);
        return Results.Empty;
    }

    private async Task<(IdempotencyRecord? Completed, IResult? Problem)> ReserveAsync(
        int userId, string key, string endpoint, byte[] hash, CancellationToken cancellationToken)
    {
        await using var scope = scopes.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<OpsFlowDbContext>();
        var now = clock.GetUtcNow();

        var existing = await db.IdempotencyRecords.SingleOrDefaultAsync(r => r.UserId == userId && r.Key == key, cancellationToken);
        if (existing is not null && existing.ExpiresAt <= now)
        {
            db.IdempotencyRecords.Remove(existing);
            await db.SaveChangesAsync(cancellationToken);
            existing = null;
        }

        if (existing is not null)
        {
            if (existing.Endpoint != endpoint || !existing.RequestHash.AsSpan().SequenceEqual(hash))
            {
                return (null, Problems.Create(
                    StatusCodes.Status422UnprocessableEntity,
                    "idempotency.key_reused",
                    "Idempotency-Key reused",
                    "This key was already used for a different request. Use a new key for a new command."));
            }

            return existing.IsCompleted ? (existing, null) : (null, InProgress());
        }

        db.IdempotencyRecords.Add(IdempotencyRecord.Reserve(userId, key, endpoint, hash, now, Retention));
        try
        {
            await db.SaveChangesAsync(cancellationToken);
            return (null, null);
        }
        catch (DbUpdateException exception) when (exception.InnerException is SqlException { Number: 2601 or 2627 })
        {
            return (null, InProgress()); // a concurrent request with the same key won the reservation
        }
    }

    private async Task CompleteAsync(int userId, string key, HttpResponse response, byte[] body)
    {
        await using var scope = scopes.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<OpsFlowDbContext>();
        var record = await db.IdempotencyRecords.SingleAsync(r => r.UserId == userId && r.Key == key);
        record.Complete(
            response.StatusCode,
            response.ContentType,
            body,
            response.Headers.ETag.ToString() is { Length: > 0 } etag ? etag : null,
            response.Headers.Location.ToString() is { Length: > 0 } location ? location : null);

        // Not tied to the request's cancellation: the command already happened and must be recorded.
        await db.SaveChangesAsync(CancellationToken.None);
    }

    private async Task ReleaseAsync(int userId, string key)
    {
        await using var scope = scopes.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<OpsFlowDbContext>();
        await db.IdempotencyRecords
            .Where(r => r.UserId == userId && r.Key == key && r.StatusCode == null)
            .ExecuteDeleteAsync(CancellationToken.None);
    }

    private static async Task<byte[]> ComputeRequestHashAsync(HttpRequest request, CancellationToken cancellationToken)
    {
        if (!request.Body.CanSeek)
        {
            throw new InvalidOperationException("Request buffering must be enabled for idempotent endpoints (UseIdempotencyBuffering).");
        }

        request.Body.Position = 0;
        using var sha = IncrementalHash.CreateHash(HashAlgorithmName.SHA256);
        sha.AppendData(Encoding.UTF8.GetBytes($"{request.Method}\n{request.Path}\n{request.Headers.IfMatch}\n"));
        var chunk = new byte[8192];
        int read;
        while ((read = await request.Body.ReadAsync(chunk, cancellationToken)) > 0)
        {
            sha.AppendData(chunk, 0, read);
        }

        request.Body.Position = 0;
        return sha.GetHashAndReset();
    }

    private static Microsoft.AspNetCore.Http.HttpResults.ProblemHttpResult InProgress() => Problems.Create(
        StatusCodes.Status409Conflict,
        "idempotency.in_progress",
        "Request in progress",
        "A request with this Idempotency-Key is still being processed. Retry shortly.");

    [GeneratedRegex("^[A-Za-z0-9._:-]{1,100}$")]
    private static partial Regex ValidKey();

    /// <summary>Writes a stored response back exactly as it was first sent.</summary>
    private sealed class ReplayedResult(IdempotencyRecord record) : IResult
    {
        public async Task ExecuteAsync(HttpContext httpContext)
        {
            var response = httpContext.Response;
            response.StatusCode = record.StatusCode!.Value;
            response.ContentType = record.ResponseContentType;
            if (record.ResponseETag is not null)
            {
                response.Headers.ETag = record.ResponseETag;
            }

            if (record.ResponseLocation is not null)
            {
                response.Headers.Location = record.ResponseLocation;
            }

            response.Headers[ReplayedHeader] = "true";
            await response.Body.WriteAsync(record.ResponseBody ?? [], httpContext.RequestAborted);
        }
    }
}

public static class IdempotencyExtensions
{
    /// <summary>Add before other filters so that validation failures are recorded and replayed too.</summary>
    public static RouteHandlerBuilder WithIdempotency(this RouteHandlerBuilder builder) =>
        builder.AddEndpointFilter<IdempotencyFilter>();

    /// <summary>Lets the filter hash the raw body after model binding has read it.</summary>
    public static IApplicationBuilder UseIdempotencyBuffering(this IApplicationBuilder app) =>
        app.Use((context, next) =>
        {
            if (HttpMethods.IsPost(context.Request.Method) && context.Request.Headers.ContainsKey(IdempotencyFilter.HeaderName))
            {
                context.Request.EnableBuffering();
            }

            return next(context);
        });
}
