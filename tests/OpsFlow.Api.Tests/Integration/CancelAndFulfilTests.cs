using System.Net;
using OpsFlow.Api.Features.Requests;
using OpsFlow.Domain.Requests;
using static OpsFlow.Api.Tests.Integration.TestUsers;

namespace OpsFlow.Api.Tests.Integration;

[TestFixture]
public class CancelAndFulfilTests : IntegrationTestBase
{
    [Test]
    public async Task Cancel_InApproval_CancelsAndSkipsPendingSteps()
    {
        var (submitted, etag) = await SubmittedRequestAsync(Employee3, 75_000m);
        using var requester = ClientFor(Employee3);

        using var response = await SendCommandAsync(requester, HttpMethod.Post, $"/api/v1/requests/{submitted.Id}/cancel", etag);

        Assert.That(response.StatusCode, Is.EqualTo(HttpStatusCode.OK), await response.Content.ReadAsStringAsync());
        var body = await ReadAsync<RequestDetailsResponse>(response);
        Assert.Multiple(() =>
        {
            Assert.That(body.Status, Is.EqualTo(RequestStatus.Cancelled));
            Assert.That(body.CompletedAt, Is.Not.Null);
            Assert.That(body.Steps.Select(s => s.Status), Is.All.EqualTo(StepStatus.Skipped));
        });
    }

    [Test]
    public async Task Cancel_ByRequestersManager_Returns403()
    {
        var (created, etag) = await CreateRequestAsync(Employee1);
        using var manager = ClientFor(Manager1);

        using var response = await SendCommandAsync(manager, HttpMethod.Post, $"/api/v1/requests/{created.Id}/cancel", etag);

        Assert.That(response.StatusCode, Is.EqualTo(HttpStatusCode.Forbidden));
        Assert.That((await ReadProblemAsync(response)).Code, Is.EqualTo("request.not_owner"));
    }

    [Test]
    public async Task Fulfil_ApprovedRequestByFinance_IsFulfilled()
    {
        var (submitted, _) = await SubmittedRequestAsync(Employee1);
        var (approved, etag) = await DecideCurrentStepAsync(Manager1, submitted.Id);
        using var finance = ClientFor(Finance1);

        using var response = await SendCommandAsync(finance, HttpMethod.Post, $"/api/v1/requests/{approved.Id}/fulfil", etag);

        Assert.That(response.StatusCode, Is.EqualTo(HttpStatusCode.OK), await response.Content.ReadAsStringAsync());
        Assert.That((await ReadAsync<RequestDetailsResponse>(response)).Status, Is.EqualTo(RequestStatus.Fulfilled));
    }

    [Test]
    public async Task Fulfil_ByDirector_Returns403FromPolicy()
    {
        var (submitted, _) = await SubmittedRequestAsync(Employee1);
        var (approved, etag) = await DecideCurrentStepAsync(Manager1, submitted.Id);
        using var director = ClientFor(Director);

        using var response = await SendCommandAsync(director, HttpMethod.Post, $"/api/v1/requests/{approved.Id}/fulfil", etag);

        Assert.That(response.StatusCode, Is.EqualTo(HttpStatusCode.Forbidden));
        Assert.That((await ReadProblemAsync(response)).Code, Is.EqualTo("auth.forbidden"));
    }

    [Test]
    public async Task Fulfil_OwnRequest_Returns403SelfFulfilment()
    {
        var (submitted, _) = await SubmittedRequestAsync(Finance1, 1_000m);
        var (approved, etag) = await DecideCurrentStepAsync(Director, submitted.Id);
        using var self = ClientFor(Finance1);

        using var response = await SendCommandAsync(self, HttpMethod.Post, $"/api/v1/requests/{approved.Id}/fulfil", etag);

        Assert.That(response.StatusCode, Is.EqualTo(HttpStatusCode.Forbidden));
        Assert.That((await ReadProblemAsync(response)).Code, Is.EqualTo("request.self_fulfilment"));
    }

    [Test]
    public async Task Fulfil_NotYetApproved_Returns409()
    {
        var (submitted, etag) = await SubmittedRequestAsync(Employee1);
        using var finance = ClientFor(Finance1);

        using var response = await SendCommandAsync(finance, HttpMethod.Post, $"/api/v1/requests/{submitted.Id}/fulfil", etag);

        Assert.That(response.StatusCode, Is.EqualTo(HttpStatusCode.Conflict));
    }
}
