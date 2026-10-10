using System.Net;
using Microsoft.EntityFrameworkCore;
using OpsFlow.Api.Features.Requests;
using OpsFlow.Domain.Requests;
using static OpsFlow.Api.Tests.Integration.TestUsers;

namespace OpsFlow.Api.Tests.Integration;

[TestFixture]
public class IdempotencyTests : IntegrationTestBase
{
    private static readonly object Body = new { title = "Idempotent laptop", category = "Purchase", amount = 1_000m };

    // AC-14
    [Test]
    public async Task Create_RetriedWithSameKey_ReplaysResponseAndCreatesOnce()
    {
        using var client = ClientFor(Employee1);

        using var first = await SendCommandAsync(client, HttpMethod.Post, "/api/v1/requests", body: Body, idempotencyKey: "create-1");
        using var retry = await SendCommandAsync(client, HttpMethod.Post, "/api/v1/requests", body: Body, idempotencyKey: "create-1");

        Assert.Multiple(async () =>
        {
            Assert.That(first.StatusCode, Is.EqualTo(HttpStatusCode.Created));
            Assert.That(retry.StatusCode, Is.EqualTo(HttpStatusCode.Created));
            Assert.That(await retry.Content.ReadAsStringAsync(), Is.EqualTo(await first.Content.ReadAsStringAsync()));
            Assert.That(retry.Headers.Location, Is.EqualTo(first.Headers.Location));
            Assert.That(retry.Headers.ETag, Is.EqualTo(first.Headers.ETag));
            Assert.That(retry.Headers.GetValues("Idempotent-Replayed").Single(), Is.EqualTo("true"));
            Assert.That(first.Headers.Contains("Idempotent-Replayed"), Is.False);
            Assert.That(await QueryDbAsync(db => db.Requests.CountAsync()), Is.EqualTo(1));
        });
    }

    [Test]
    public async Task SameKeyWithDifferentBody_Returns422()
    {
        using var client = ClientFor(Employee1);
        using var first = await SendCommandAsync(client, HttpMethod.Post, "/api/v1/requests", body: Body, idempotencyKey: "create-2");

        using var reused = await SendCommandAsync(client, HttpMethod.Post, "/api/v1/requests",
            body: new { title = "Something else", category = "Purchase", amount = 5m }, idempotencyKey: "create-2");

        Assert.That(reused.StatusCode, Is.EqualTo(HttpStatusCode.UnprocessableEntity));
        Assert.That((await ReadProblemAsync(reused)).Code, Is.EqualTo("idempotency.key_reused"));
    }

    [Test]
    public async Task SameKeyFromDifferentUsers_AreIndependent()
    {
        using var employee1 = ClientFor(Employee1);
        using var employee2 = ClientFor(Employee2);

        using var a = await SendCommandAsync(employee1, HttpMethod.Post, "/api/v1/requests", body: Body, idempotencyKey: "shared-key");
        using var b = await SendCommandAsync(employee2, HttpMethod.Post, "/api/v1/requests", body: Body, idempotencyKey: "shared-key");

        Assert.Multiple(async () =>
        {
            Assert.That(b.StatusCode, Is.EqualTo(HttpStatusCode.Created));
            Assert.That(b.Headers.Contains("Idempotent-Replayed"), Is.False);
            Assert.That(await QueryDbAsync(db => db.Requests.CountAsync()), Is.EqualTo(2));
        });
    }

    [Test]
    public async Task Decision_RetriedAfterSuccess_ReplaysOkInsteadOf412()
    {
        var (submitted, etag) = await SubmittedRequestAsync(Employee1);
        using var manager = ClientFor(Manager1);
        var url = $"/api/v1/requests/{submitted.Id}/steps/{submitted.Steps[0].Id}/decision";
        var body = new { decision = "Approve" };

        using var first = await SendCommandAsync(manager, HttpMethod.Post, url, etag, body, "decide-1");
        using var retry = await SendCommandAsync(manager, HttpMethod.Post, url, etag, body, "decide-1");
        using var withoutKey = await SendCommandAsync(manager, HttpMethod.Post, url, etag, body);

        Assert.Multiple(async () =>
        {
            Assert.That(first.StatusCode, Is.EqualTo(HttpStatusCode.OK));
            Assert.That(retry.StatusCode, Is.EqualTo(HttpStatusCode.OK), "the retry gets the original answer");
            Assert.That((await ReadAsync<RequestDetailsResponse>(retry)).Status, Is.EqualTo(RequestStatus.Approved));
            Assert.That(withoutKey.StatusCode, Is.EqualTo(HttpStatusCode.PreconditionFailed), "without a key, the stale ETag is caught");
        });
    }

    [Test]
    public async Task ValidationFailure_IsAlsoReplayed()
    {
        using var client = ClientFor(Employee1);
        var invalid = new { title = "x", category = "Purchase", amount = 1m };

        using var first = await SendCommandAsync(client, HttpMethod.Post, "/api/v1/requests", body: invalid, idempotencyKey: "invalid-1");
        using var retry = await SendCommandAsync(client, HttpMethod.Post, "/api/v1/requests", body: invalid, idempotencyKey: "invalid-1");

        Assert.Multiple(() =>
        {
            Assert.That(first.StatusCode, Is.EqualTo(HttpStatusCode.BadRequest));
            Assert.That(retry.StatusCode, Is.EqualTo(HttpStatusCode.BadRequest));
            Assert.That(retry.Headers.Contains("Idempotent-Replayed"), Is.True);
        });
    }

    [TestCase("has spaces")]
    [TestCase("way-too-long-key-way-too-long-key-way-too-long-key-way-too-long-key-way-too-long-key-way-too-long-key-x")]
    public async Task InvalidKey_Returns400(string key)
    {
        using var client = ClientFor(Employee1);

        using var response = await SendCommandAsync(client, HttpMethod.Post, "/api/v1/requests", body: Body, idempotencyKey: key);

        Assert.That(response.StatusCode, Is.EqualTo(HttpStatusCode.BadRequest));
        Assert.That((await ReadProblemAsync(response)).Code, Is.EqualTo("idempotency.invalid_key"));
    }

    [Test]
    [Repeat(10)]
    public async Task ConcurrentRequestsWithSameKey_CreateExactlyOneRequest()
    {
        using var clientA = ClientFor(Employee1);
        using var clientB = ClientFor(Employee1);

        var responses = await Task.WhenAll(
            SendCommandAsync(clientA, HttpMethod.Post, "/api/v1/requests", body: Body, idempotencyKey: "race-1"),
            SendCommandAsync(clientB, HttpMethod.Post, "/api/v1/requests", body: Body, idempotencyKey: "race-1"));

        try
        {
            Assert.That(responses.Select(r => r.StatusCode), Has.Some.EqualTo(HttpStatusCode.Created));
            Assert.That(responses.Select(r => r.StatusCode), Is.All.EqualTo(HttpStatusCode.Created).Or.EqualTo(HttpStatusCode.Conflict));
            Assert.That(await QueryDbAsync(db => db.Requests.CountAsync()), Is.EqualTo(1));
        }
        finally
        {
            foreach (var response in responses)
            {
                response.Dispose();
            }
        }
    }
}
