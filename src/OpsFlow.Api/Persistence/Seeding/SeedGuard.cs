namespace OpsFlow.Api.Persistence.Seeding;

public static class SeedGuard
{
    /// <summary>Demo accounts share a known password; they must never exist in production.</summary>
    public static void EnsureSeedingAllowed(IHostEnvironment environment, SeedOptions options)
    {
        ArgumentNullException.ThrowIfNull(environment);
        ArgumentNullException.ThrowIfNull(options);

        if (environment.IsProduction())
        {
            throw new InvalidOperationException("Demo data seeding is not allowed in the Production environment.");
        }

        if (string.IsNullOrWhiteSpace(options.DemoPassword) || options.DemoPassword.Length < SeedOptions.MinimumPasswordLength)
        {
            throw new InvalidOperationException(
                $"Seed:DemoPassword must be configured with at least {SeedOptions.MinimumPasswordLength} characters when seeding is enabled.");
        }
    }
}
