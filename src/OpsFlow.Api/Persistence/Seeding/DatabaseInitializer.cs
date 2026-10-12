using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;
using OpsFlow.Api.Infrastructure.Correlation;

namespace OpsFlow.Api.Persistence.Seeding;

public static class DatabaseInitializer
{
    /// <summary>Applies migrations and seeds demo data, depending on configuration (NFR-04).</summary>
    public static async Task InitializeDatabaseAsync(this WebApplication app, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(app);

        await using var scope = app.Services.CreateAsyncScope();
        var services = scope.ServiceProvider;
        services.GetRequiredService<CorrelationContext>().CorrelationId = "startup";

        var logger = services.GetRequiredService<ILoggerFactory>().CreateLogger(typeof(DatabaseInitializer));
        var database = services.GetRequiredService<IOptions<DatabaseOptions>>().Value;
        var seed = services.GetRequiredService<IOptions<SeedOptions>>().Value;

        if (database.MigrateOnStartup)
        {
            logger.LogInformation("Applying database migrations.");
            // EF Core takes a database-level lock, so concurrent instances do not race.
            await services.GetRequiredService<OpsFlowDbContext>().Database.MigrateAsync(cancellationToken);
        }

        if (seed.Enabled)
        {
            SeedGuard.EnsureSeedingAllowed(app.Environment, seed);
            await services.GetRequiredService<DemoDataSeeder>().SeedAsync(seed.DemoPassword!, cancellationToken);
        }
    }
}
