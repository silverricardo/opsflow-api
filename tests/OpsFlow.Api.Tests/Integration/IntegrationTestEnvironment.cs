using Microsoft.Data.SqlClient;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using OpsFlow.Api.Persistence;
using Testcontainers.MsSql;

namespace OpsFlow.Api.Tests.Integration;

/// <summary>
/// One SQL Server container and one API host per test run (ADR-007). Migrations are applied by the
/// API at startup exactly as in a real environment; tests reset request data before each test.
/// </summary>
[SetUpFixture]
public sealed class IntegrationTestEnvironment
{
    /// <summary>Same image as docker-compose.yml. Override with OPSFLOW_TEST_SQL_IMAGE (e.g. for SQL Server 2025).</summary>
    public const string DefaultSqlServerImage = "mcr.microsoft.com/mssql/server:2022-CU27-ubuntu-22.04";

    private static MsSqlContainer? _container;
    private static OpsFlowApiFactory? _factory;
    private static string? _connectionString;

    internal static OpsFlowApiFactory Factory =>
        _factory ?? throw new InvalidOperationException("The integration test environment has not started.");

    internal static string ConnectionString =>
        _connectionString ?? throw new InvalidOperationException("The integration test environment has not started.");

    [OneTimeSetUp]
    public async Task StartAsync()
    {
        var image = Environment.GetEnvironmentVariable("OPSFLOW_TEST_SQL_IMAGE") ?? DefaultSqlServerImage;
        _container = new MsSqlBuilder(image).Build();
        await _container.StartAsync();

        _connectionString = new SqlConnectionStringBuilder(_container.GetConnectionString())
        {
            InitialCatalog = "OpsFlowTests",
        }.ConnectionString;

        _factory = new OpsFlowApiFactory(_connectionString);
        _ = _factory.Server; // starts the host: applies migrations
        await TestUsers.SeedAsync(_factory.Services);
    }

    [OneTimeTearDown]
    public async Task StopAsync()
    {
        if (_factory is not null)
        {
            await _factory.DisposeAsync();
        }

        if (_container is not null)
        {
            await _container.DisposeAsync();
        }
    }

    /// <summary>
    /// Removes request data between tests; users and reference data stay. TRUNCATE is used for the
    /// audit table because its trigger blocks DELETE — by design, the application login cannot do this.
    /// </summary>
    internal static async Task ResetDataAsync()
    {
        await using var scope = Factory.Services.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<OpsFlowDbContext>();
        await db.Database.ExecuteSqlRawAsync(
            "TRUNCATE TABLE [AuditEntries]; DELETE FROM [ApprovalSteps]; DELETE FROM [Requests];");
    }
}
