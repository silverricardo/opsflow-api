using Microsoft.AspNetCore.Diagnostics;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;

namespace OpsFlow.Api.Infrastructure.Errors;

/// <summary>
/// Last line of defence: maps known exceptions to problem responses and hides internals for the rest.
/// </summary>
public sealed partial class GlobalExceptionHandler(IProblemDetailsService problemDetails, ILogger<GlobalExceptionHandler> logger)
    : IExceptionHandler
{
    public async ValueTask<bool> TryHandleAsync(HttpContext httpContext, Exception exception, CancellationToken cancellationToken)
    {
        if (exception is OperationCanceledException && httpContext.RequestAborted.IsCancellationRequested)
        {
            // The client went away; there is nobody to answer.
            LogCancelled(logger);
            return true;
        }

        var (status, code, title) = exception switch
        {
            // Lost the optimistic-concurrency race between load and save (BR-60).
            DbUpdateConcurrencyException => (StatusCodes.Status412PreconditionFailed, ErrorCodes.ETagMismatch, "Precondition failed"),
            BadHttpRequestException bad => (bad.StatusCode, ErrorCodes.ForStatus(bad.StatusCode), "Bad request"),
            _ => (StatusCodes.Status500InternalServerError, ErrorCodes.ServerError, "An unexpected error occurred"),
        };

        if (status >= StatusCodes.Status500InternalServerError)
        {
            LogUnhandled(logger, exception);
        }
        else
        {
            LogHandled(logger, status, code);
        }

        httpContext.Response.StatusCode = status;
        return await problemDetails.TryWriteAsync(new ProblemDetailsContext
        {
            HttpContext = httpContext,
            ProblemDetails = new ProblemDetails
            {
                Status = status,
                Title = title,
                Detail = status == StatusCodes.Status412PreconditionFailed
                    ? "The request was modified by someone else. Reload it and try again."
                    : null,
                Extensions = { ["code"] = code },
            },
        });
    }

    [LoggerMessage(Level = LogLevel.Information, Message = "Request was cancelled by the client.")]
    private static partial void LogCancelled(ILogger logger);

    [LoggerMessage(Level = LogLevel.Error, Message = "Unhandled exception.")]
    private static partial void LogUnhandled(ILogger logger, Exception exception);

    [LoggerMessage(Level = LogLevel.Information, Message = "Request failed with {StatusCode} ({ErrorCode}).")]
    private static partial void LogHandled(ILogger logger, int statusCode, string errorCode);
}
