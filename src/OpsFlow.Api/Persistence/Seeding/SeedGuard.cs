namespace OpsFlow.Api.Persistence.Seeding;

public static class SeedGuard
{
    /// <summary>Environments where demo or load-test accounts may be created. Anything else is refused.</summary>
    public static readonly IReadOnlySet<string> AllowedEnvironments =
        new HashSet<string>(["Development", "Testing", "Performance"], StringComparer.OrdinalIgnoreCase);

    /// <summary>
    /// Seeded accounts share a known password, including an Admin: they must never exist in a real
    /// environment. An allow-list (not "anything but Production") so that Staging or a typo is refused too.
    /// </summary>
    public static void EnsureSeedingAllowed(IHostEnvironment environment, SeedOptions options)
    {
        ArgumentNullException.ThrowIfNull(environment);
        ArgumentNullException.ThrowIfNull(options);

        if (!AllowedEnvironments.Contains(environment.EnvironmentName))
        {
            throw new InvalidOperationException(
                $"Seeding is not allowed in the '{environment.EnvironmentName}' environment (allowed: {string.Join(", ", AllowedEnvironments)}).");
        }

        if (string.IsNullOrWhiteSpace(options.DemoPassword) || options.DemoPassword.Length < SeedOptions.MinimumPasswordLength)
        {
            throw new InvalidOperationException(
                $"Seed:DemoPassword must be configured with at least {SeedOptions.MinimumPasswordLength} characters when seeding is enabled.");
        }
    }
}
