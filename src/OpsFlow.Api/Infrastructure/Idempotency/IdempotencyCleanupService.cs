using Microsoft.EntityFrameworkCore;
using OpsFlow.Api.Persistence;

namespace OpsFlow.Api.Infrastructure.Idempotency;

/// <summary>Deletes expired idempotency records every hour. Safe to run on every instance.</summary>
public sealed partial class IdempotencyCleanupService(
    IServiceScopeFactory scopes,
    TimeProvider clock,
    ILogger<IdempotencyCleanupService> logger) : BackgroundService
{
    private static readonly TimeSpan Interval = TimeSpan.FromHours(1);

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        using var timer = new PeriodicTimer(Interval, clock);
        while (await timer.WaitForNextTickAsync(stoppingToken))
        {
            try
            {
                await using var scope = scopes.CreateAsyncScope();
                var db = scope.ServiceProvider.GetRequiredService<OpsFlowDbContext>();
                var now = clock.GetUtcNow();
                var deleted = await db.IdempotencyRecords.Where(r => r.ExpiresAt < now).ExecuteDeleteAsync(stoppingToken);
                LogCleanup(logger, deleted);
            }
            catch (Exception exception) when (exception is not OperationCanceledException)
            {
                LogCleanupFailed(logger, exception);
            }
        }
    }

    [LoggerMessage(Level = LogLevel.Information, Message = "Deleted {Count} expired idempotency records.")]
    private static partial void LogCleanup(ILogger logger, int count);

    [LoggerMessage(Level = LogLevel.Warning, Message = "Idempotency cleanup failed; it will be retried on the next tick.")]
    private static partial void LogCleanupFailed(ILogger logger, Exception exception);
}
