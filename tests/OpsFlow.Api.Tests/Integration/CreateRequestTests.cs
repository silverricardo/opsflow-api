using System.Net;
using System.Net.Http.Json;
using Microsoft.EntityFrameworkCore;
using OpsFlow.Api.Features.Requests;
using OpsFlow.Api.Persistence;
using OpsFlow.Domain.Auditing;
using OpsFlow.Domain.Requests;
using static OpsFlow.Api.Tests.Integration.TestUsers;

namespace OpsFlow.Api.Tests.Integration;

[TestFixture]
public class CreateRequestTests : IntegrationTestBase
{
    private const string Url = "/api/v1/requests";

    [Test]
    public async Task Create_ReturnsCreatedDraftWithLocationETagAndReference()
    {
        using var client = ClientFor(Employee1);

        using var response = await client.PostAsJsonAsync(
            Url, new { title = "  Standing desk ", description = "For the new office", category = "Purchase", amount = 499.99m });

        Assert.That(response.StatusCode, Is.EqualTo(HttpStatusCode.Created));
        var body = await ReadAsync<RequestDetailsResponse>(response);
        Assert.Multiple(() =>
        {
            Assert.That(response.Headers.Location?.ToString(), Is.EqualTo($"/api/v1/requests/{body.Id}"));
            Assert.That(response.Headers.ETag?.IsWeak, Is.False);
            Assert.That(body.Reference, Does.Match(@"^REQ-\d{6}$"));
            Assert.That(body.Title, Is.EqualTo("Standing desk"));
            Assert.That(body.Status, Is.EqualTo(RequestStatus.Draft));
            Assert.That(body.Amount, Is.EqualTo(499.99m));
            Assert.That(body.Requester, Is.EqualTo(new UserSummary(Employee1.Id, Employee1.DisplayName)));
            Assert.That(body.Steps, Is.Empty);
        });
    }

    [Test]
    public async Task Create_WritesAuditEntryWithCallersCorrelationId()
    {
        using var client = ClientFor(Employee1);
        using var message = new HttpRequestMessage(HttpMethod.Post, Url)
        {
            Content = JsonContent.Create(new { title = "Monitor", category = "Purchase", amount = 250m }),
        };
        message.Headers.Add("X-Correlation-Id", "test-correlation-123");

        using var response = await client.SendAsync(message);
        var body = await ReadAsync<RequestDetailsResponse>(response);

        Assert.That(response.Headers.GetValues("X-Correlation-Id").Single(), Is.EqualTo("test-correlation-123"));
        var audit = await QueryDbAsync(db => db.AuditEntries
            .Where(a => a.RequestId == body.Id)
            .Select(a => new { a.Action, a.ActorId, a.ToStatus, CorrelationId = EF.Property<string>(a, OpsFlowDbContext.CorrelationIdProperty) })
            .SingleAsync());
        Assert.That(audit, Is.EqualTo(new
        {
            Action = AuditActions.RequestCreated,
            ActorId = Employee1.Id,
            ToStatus = (RequestStatus?)RequestStatus.Draft,
            CorrelationId = "test-correlation-123",
        }));
    }

    [Test]
    public async Task Create_WithInvalidFields_Returns400WithFieldErrorsAndCreatesNothing()
    {
        using var client = ClientFor(Employee1);

        using var response = await client.PostAsJsonAsync(Url, new { title = "ab", amount = 10.123m });

        Assert.That(response.StatusCode, Is.EqualTo(HttpStatusCode.BadRequest));
        var problem = await ReadProblemAsync(response);
        Assert.Multiple(async () =>
        {
            Assert.That(problem.Code, Is.EqualTo("validation.failed"));
            Assert.That(problem.Errors!.Keys, Is.EquivalentTo(new[] { "title", "category", "amount" }));
            Assert.That(await QueryDbAsync(db => db.Requests.CountAsync()), Is.Zero);
        });
    }

    [TestCase("""{ "title": "Valid", "category": "Purchase", "amount": 10, "status": "Approved" }""", TestName = "unknown property")]
    [TestCase("""{ "title": "Valid", "category": 0, "amount": 10 }""", TestName = "enum as number")]
    [TestCase("""{ "title": "Valid", "category": "Purchase", "amount": "10" }""", TestName = "number as string")]
    [TestCase("""{ "title": "Valid", """, TestName = "malformed JSON")]
    public async Task Create_WithMalformedOrUnexpectedJson_Returns400Problem(string json)
    {
        using var client = ClientFor(Employee1);

        using var response = await client.PostAsync(
            new Uri(Url, UriKind.Relative), new StringContent(json, System.Text.Encoding.UTF8, "application/json"));

        Assert.That(response.StatusCode, Is.EqualTo(HttpStatusCode.BadRequest));
        Assert.That((await ReadProblemAsync(response)).Code, Is.EqualTo("request.malformed"));
    }

    [Test]
    public async Task Create_AsAdmin_Returns403()
    {
        using var client = ClientFor(Admin);

        using var response = await client.PostAsJsonAsync(Url, new { title = "Valid", category = "Expense", amount = 10m });

        Assert.That(response.StatusCode, Is.EqualTo(HttpStatusCode.Forbidden));
        Assert.That((await ReadProblemAsync(response)).Code, Is.EqualTo("request.admin_cannot_create"));
    }

    [Test]
    public async Task Create_TwoRequests_GetDistinctReferences()
    {
        var (first, _) = await CreateRequestAsync(Employee1);
        var (second, _) = await CreateRequestAsync(Employee1);

        Assert.That(second.Reference, Is.Not.EqualTo(first.Reference));
    }
}
