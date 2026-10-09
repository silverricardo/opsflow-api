using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;

namespace OpsFlow.Api.Tests.Integration;

/// <summary>Hosts the real API in-process against the test SQL Server, with optional setting overrides.</summary>
internal sealed class OpsFlowApiFactory(string connectionString, IReadOnlyDictionary<string, string?>? overrides = null)
    : WebApplicationFactory<Program>
{
    /// <summary>Test-only key. Not a secret: it signs tokens that are only valid against this test host.</summary>
    public const string SigningKey = "integration-tests-signing-key-0123456789-abcdef";

    protected override void ConfigureWebHost(IWebHostBuilder builder)
    {
        builder.UseEnvironment("Testing");

        var settings = new Dictionary<string, string?>
        {
            ["ConnectionStrings:OpsFlow"] = connectionString,
            ["Jwt:SigningKey"] = SigningKey,
            ["Database:MigrateOnStartup"] = "true",
            ["Seed:Enabled"] = "false",
            ["RateLimiting:TokenPermitLimit"] = "10000",
            ["RateLimiting:GlobalTokenLimit"] = "10000",
            ["RateLimiting:GlobalTokensPerSecond"] = "10000",
            ["Serilog:MinimumLevel:Default"] = Environment.GetEnvironmentVariable("OPSFLOW_TEST_LOG_LEVEL") ?? "Error",
        };

        foreach (var (key, value) in overrides ?? new Dictionary<string, string?>())
        {
            settings[key] = value;
        }

        foreach (var (key, value) in settings)
        {
            builder.UseSetting(key, value);
        }
    }
}
