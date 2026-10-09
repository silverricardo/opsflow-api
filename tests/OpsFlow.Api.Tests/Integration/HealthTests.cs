using System.Net;

namespace OpsFlow.Api.Tests.Integration;

[TestFixture]
public class HealthTests : IntegrationTestBase
{
    [TestCase("/health/live")]
    [TestCase("/health/ready")]
    public async Task Health_WhenDatabaseIsUp_ReturnsHealthy(string path)
    {
        using var client = AnonymousClient();

        using var response = await client.GetAsync(new Uri(path, UriKind.Relative));

        Assert.That(response.StatusCode, Is.EqualTo(HttpStatusCode.OK));
        Assert.That(await response.Content.ReadAsStringAsync(), Does.Contain("\"status\":\"Healthy\""));
    }

    [Test]
    public async Task Health_WhenDatabaseIsUnreachable_ReadyFailsButLiveSucceeds()
    {
        await using var factory = new OpsFlowApiFactory(
            "Server=127.0.0.1,1;Database=OpsFlow;User Id=sa;Password=unused;Connect Timeout=1;TrustServerCertificate=True",
            new Dictionary<string, string?> { ["Database:MigrateOnStartup"] = "false" });
        using var client = factory.CreateClient();

        using var live = await client.GetAsync(new Uri("/health/live", UriKind.Relative));
        using var ready = await client.GetAsync(new Uri("/health/ready", UriKind.Relative));

        Assert.Multiple(async () =>
        {
            Assert.That(live.StatusCode, Is.EqualTo(HttpStatusCode.OK));
            Assert.That(ready.StatusCode, Is.EqualTo(HttpStatusCode.ServiceUnavailable));
            Assert.That(await ready.Content.ReadAsStringAsync(), Does.Not.Contain("Exception"), "no internals leak");
        });
    }
}
