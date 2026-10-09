namespace OpsFlow.Api.Persistence.Seeding;

public sealed class DatabaseOptions
{
    public const string SectionName = "Database";

    /// <summary>
    /// Apply EF Core migrations at startup. Convenient for local and demo environments; production
    /// applies a migration bundle from the deployment pipeline instead (see docs/deployment).
    /// </summary>
    public bool MigrateOnStartup { get; set; }
}

public sealed class SeedOptions
{
    public const string SectionName = "Seed";
    public const int MinimumPasswordLength = 12;

    /// <summary>Create demo users and requests on an empty database. Refused in Production.</summary>
    public bool Enabled { get; set; }

    /// <summary>Password for every demo user. Secret: comes from configuration, never from source code.</summary>
    public string? DemoPassword { get; set; }
}
