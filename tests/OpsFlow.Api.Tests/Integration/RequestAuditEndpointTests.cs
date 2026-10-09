using System.Net;
using OpsFlow.Api.Features.Common;
using OpsFlow.Api.Features.Requests;
using OpsFlow.Domain.Auditing;
using OpsFlow.Domain.Requests;
using static OpsFlow.Api.Tests.Integration.TestUsers;

namespace OpsFlow.Api.Tests.Integration;

[TestFixture]
public class RequestAuditEndpointTests : IntegrationTestBase
{
    [Test]
    public async Task Audit_ReturnsHistoryOldestFirstWithActorsAndDetails()
    {
        var (submitted, _) = await SubmittedRequestAsync(Employee1);
        await DecideCurrentStepAsync(Manager1, submitted.Id, "Approve", "OK for me");
        using var client = ClientFor(Employee1);

        using var response = await client.GetAsync(new Uri($"/api/v1/requests/{submitted.Id}/audit", UriKind.Relative));

        Assert.That(response.StatusCode, Is.EqualTo(HttpStatusCode.OK));
        var page = await ReadAsync<PagedResponse<AuditEntryResponse>>(response);
        Assert.Multiple(() =>
        {
            Assert.That(page.TotalCount, Is.EqualTo(3));
            Assert.That(page.Items.Select(e => e.Action),
                Is.EqualTo(new[] { AuditActions.RequestCreated, AuditActions.RequestSubmitted, AuditActions.StepApproved }));
            Assert.That(page.Items.Select(e => e.Actor.Id), Is.EqualTo(new[] { Employee1.Id, Employee1.Id, Manager1.Id }));
            Assert.That(page.Items[2].FromStatus, Is.EqualTo(RequestStatus.InApproval));
            Assert.That(page.Items[2].ToStatus, Is.EqualTo(RequestStatus.Approved));
            Assert.That(page.Items[2].Details?.GetProperty("comment").GetString(), Is.EqualTo("OK for me"));
            Assert.That(page.Items.All(e => !string.IsNullOrEmpty(e.CorrelationId)), Is.True);
        });
    }

    [Test]
    public async Task Audit_IsPaged()
    {
        var (submitted, _) = await SubmittedRequestAsync(Employee1);
        using var client = ClientFor(Employee1);

        using var response = await client.GetAsync(new Uri($"/api/v1/requests/{submitted.Id}/audit?page=2&pageSize=1", UriKind.Relative));

        var page = await ReadAsync<PagedResponse<AuditEntryResponse>>(response);
        Assert.Multiple(() =>
        {
            Assert.That(page.Page, Is.EqualTo(2));
            Assert.That(page.PageSize, Is.EqualTo(1));
            Assert.That(page.TotalCount, Is.EqualTo(2));
            Assert.That(page.TotalPages, Is.EqualTo(2));
            Assert.That(page.Items.Single().Action, Is.EqualTo(AuditActions.RequestSubmitted));
        });
    }

    [TestCase("pageSize=101")]
    [TestCase("pageSize=0")]
    [TestCase("page=0")]
    public async Task Audit_WithInvalidPaging_Returns400(string query)
    {
        var (created, _) = await CreateRequestAsync(Employee1);
        using var client = ClientFor(Employee1);

        using var response = await client.GetAsync(new Uri($"/api/v1/requests/{created.Id}/audit?{query}", UriKind.Relative));

        Assert.That(response.StatusCode, Is.EqualTo(HttpStatusCode.BadRequest));
        Assert.That((await ReadProblemAsync(response)).Code, Is.EqualTo("validation.failed"));
    }

    [Test]
    public async Task Audit_OfRequestNotVisible_Returns404()
    {
        var (created, _) = await CreateRequestAsync(Employee1);
        using var client = ClientFor(Employee2);

        using var response = await client.GetAsync(new Uri($"/api/v1/requests/{created.Id}/audit", UriKind.Relative));

        Assert.That(response.StatusCode, Is.EqualTo(HttpStatusCode.NotFound));
    }
}
