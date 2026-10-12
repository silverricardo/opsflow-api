using System.Data.Common;
using Microsoft.AspNetCore.Http;
using Microsoft.EntityFrameworkCore.Diagnostics;
using OpsFlow.Api.Persistence;

namespace OpsFlow.Api.Tests.Integration;

/// <summary>
/// Fault injection: for requests carrying <see cref="HeaderName"/>, the first query after a successful
/// commit throws — the "command saved, response failed" case (e.g. a client disconnecting mid-response).
/// </summary>
internal sealed class FailAfterCommitInterceptor(IHttpContextAccessor httpContextAccessor) : DbCommandInterceptor
{
    public const string HeaderName = "X-Test-Fail-After-Commit";

    public override ValueTask<InterceptionResult<DbDataReader>> ReaderExecutingAsync(
        DbCommand command, CommandEventData eventData, InterceptionResult<DbDataReader> result, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(command);
        ArgumentNullException.ThrowIfNull(eventData);

        var flagged = httpContextAccessor.HttpContext?.Request.Headers.ContainsKey(HeaderName) == true;
        if (flagged && eventData.Context is OpsFlowDbContext { HasCommittedChanges: true })
        {
            throw new InvalidOperationException("Simulated failure after the command committed.");
        }

        return base.ReaderExecutingAsync(command, eventData, result, cancellationToken);
    }
}
