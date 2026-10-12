using System.Diagnostics;
using System.Text.RegularExpressions;
using Serilog.Context;

namespace OpsFlow.Api.Infrastructure.Correlation;

/// <summary>
/// Accepts a well-formed <c>X-Correlation-Id</c> from the caller or falls back to the W3C trace ID,
/// echoes it in the response, and attaches it to every log event of the request (NFR-02).
/// Registered first in the pipeline so that error handling and request logging also carry it.
/// </summary>
public sealed partial class CorrelationIdMiddleware(RequestDelegate next)
{
    public async Task InvokeAsync(HttpContext context, CorrelationContext correlation)
    {
        var incoming = context.Request.Headers[CorrelationContext.HeaderName].ToString();
        var correlationId = ValidCorrelationId().IsMatch(incoming)
            ? incoming
            : Activity.Current?.TraceId.ToString() ?? context.TraceIdentifier;

        correlation.CorrelationId = correlationId;
        Activity.Current?.SetTag("opsflow.correlation_id", correlationId);
        context.Response.OnStarting(() =>
        {
            context.Response.Headers[CorrelationContext.HeaderName] = correlationId;
            return Task.CompletedTask;
        });

        using (LogContext.PushProperty("CorrelationId", correlationId))
        {
            await next(context);
        }
    }

    // Untrusted input ends up in logs and the database: accept a short, safe alphabet only.
    [GeneratedRegex("^[A-Za-z0-9._-]{1,64}$")]
    private static partial Regex ValidCorrelationId();
}
