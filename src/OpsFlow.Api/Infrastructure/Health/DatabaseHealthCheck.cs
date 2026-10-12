using Microsoft.Extensions.Diagnostics.HealthChecks;
using OpsFlow.Api.Persistence;

namespace OpsFlow.Api.Infrastructure.Health;

/// <summary>
/// Readiness: can we reach SQL Server? The registration's timeout bounds the check, so connection
/// retries never keep an orchestrator's probe waiting.
/// </summary>
public sealed class DatabaseHealthCheck(OpsFlowDbContext db) : IHealthCheck
{
    public static readonly TimeSpan Timeout = TimeSpan.FromSeconds(3);

    public async Task<HealthCheckResult> CheckHealthAsync(HealthCheckContext context, CancellationToken cancellationToken = default) =>
        await db.Database.CanConnectAsync(cancellationToken)
            ? HealthCheckResult.Healthy()
            : HealthCheckResult.Unhealthy("Cannot connect to the database.");
}
