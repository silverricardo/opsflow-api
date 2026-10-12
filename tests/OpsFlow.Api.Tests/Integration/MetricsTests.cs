using System.Diagnostics.Metrics;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Diagnostics.Metrics.Testing;
using OpsFlow.Api.Infrastructure.Observability;
using static OpsFlow.Api.Tests.Integration.TestUsers;

namespace OpsFlow.Api.Tests.Integration;

[TestFixture]
public class MetricsTests : IntegrationTestBase
{
    [Test]
    public async Task BusinessMetrics_AreRecordedForCreatesAndDecisions()
    {
        var meterFactory = IntegrationTestEnvironment.Factory.Services.GetRequiredService<IMeterFactory>();
        using var created = new MetricCollector<long>(meterFactory, OpsFlowMetrics.MeterName, "opsflow.requests.created");
        using var decisions = new MetricCollector<long>(meterFactory, OpsFlowMetrics.MeterName, "opsflow.approval.decisions");

        var (submitted, _) = await SubmittedRequestAsync(Employee1);
        await DecideCurrentStepAsync(Manager1, submitted.Id, "Reject", "Not needed");

        Assert.Multiple(() =>
        {
            Assert.That(created.GetMeasurementSnapshot().Single().Tags["category"], Is.EqualTo("Purchase"));
            Assert.That(decisions.GetMeasurementSnapshot().Single().Tags["decision"], Is.EqualTo("Reject"));
        });
    }
}
