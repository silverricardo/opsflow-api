using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Design;
using OpsFlow.Api.Infrastructure.Correlation;

namespace OpsFlow.Api.Persistence;

/// <summary>
/// Used by <c>dotnet ef</c> so that design-time commands do not need the application's secrets.
/// The connection string is only used by commands that touch a database (e.g. <c>database update</c>).
/// </summary>
public sealed class DesignTimeDbContextFactory : IDesignTimeDbContextFactory<OpsFlowDbContext>
{
    public OpsFlowDbContext CreateDbContext(string[] args)
    {
        var connectionString = Environment.GetEnvironmentVariable("ConnectionStrings__OpsFlow")
            ?? "Server=localhost,1433;Database=OpsFlow;TrustServerCertificate=True";

        var options = new DbContextOptionsBuilder<OpsFlowDbContext>().UseSqlServer(connectionString).Options;
        return new OpsFlowDbContext(options, new CorrelationContext { CorrelationId = "design-time" });
    }
}
