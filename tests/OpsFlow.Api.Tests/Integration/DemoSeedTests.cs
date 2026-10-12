using System.Net;
using System.Net.Http.Json;
using Microsoft.Data.SqlClient;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using OpsFlow.Api.Persistence;
using OpsFlow.Domain.Requests;

namespace OpsFlow.Api.Tests.Integration;

/// <summary>The demo seeder builds every request through the domain, against its own empty database.</summary>
[TestFixture]
public class DemoSeedTests
{
    [Test]
    public async Task DemoSeed_OnEmptyDatabase_CreatesUsersAndRequestsInEveryStatusExceptApprovedAndDemoLoginWorks()
    {
        var connectionString = new SqlConnectionStringBuilder(IntegrationTestEnvironment.ConnectionString)
        {
            InitialCatalog = $"OpsFlowSeed_{Guid.NewGuid():N}",
        }.ConnectionString;
        const string demoPassword = "Demo-Seed-Test-Password-1";

        await using var factory = new OpsFlowApiFactory(connectionString, new Dictionary<string, string?>
        {
            ["Seed:Enabled"] = "true",
            ["Seed:DemoPassword"] = demoPassword,
        });
        using var client = factory.CreateClient(); // starts the host: migrate + seed

        await using var scope = factory.Services.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<OpsFlowDbContext>();
        try
        {
            var statuses = await db.Requests.Select(r => r.Status).Distinct().ToListAsync();
            Assert.Multiple(async () =>
            {
                Assert.That(await db.Users.CountAsync(), Is.EqualTo(10));
                Assert.That(statuses, Is.EquivalentTo(new[]
                {
                    RequestStatus.Draft, RequestStatus.InApproval, RequestStatus.ChangesRequested,
                    RequestStatus.Rejected, RequestStatus.Cancelled, RequestStatus.Fulfilled,
                }));
            });

            using var login = await client.PostAsJsonAsync("/api/v1/auth/token", new { email = "employee1@opsflow.local", password = demoPassword });
            Assert.That(login.StatusCode, Is.EqualTo(HttpStatusCode.OK));
        }
        finally
        {
            await db.Database.EnsureDeletedAsync();
        }
    }
}
