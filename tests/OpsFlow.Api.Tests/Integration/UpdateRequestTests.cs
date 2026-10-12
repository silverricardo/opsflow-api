using System.Net;
using OpsFlow.Api.Features.Requests;
using static OpsFlow.Api.Tests.Integration.TestUsers;

namespace OpsFlow.Api.Tests.Integration;

[TestFixture]
public class UpdateRequestTests : IntegrationTestBase
{
    private static readonly object ValidBody = new { title = "Updated title", category = "Expense", amount = 99.90m };

    [Test]
    public async Task Update_Draft_ReturnsUpdatedRequestAndNewETag()
    {
        var (created, etag) = await CreateRequestAsync(Employee1);
        using var client = ClientFor(Employee1);

        using var response = await UpdateAsync(client, created.Id, etag, ValidBody);

        Assert.That(response.StatusCode, Is.EqualTo(HttpStatusCode.OK));
        var body = await ReadAsync<RequestDetailsResponse>(response);
        Assert.Multiple(() =>
        {
            Assert.That(body.Title, Is.EqualTo("Updated title"));
            Assert.That(body.Amount, Is.EqualTo(99.90m));
            Assert.That(response.Headers.ETag?.Tag, Is.Not.EqualTo(etag));
        });
    }

    [Test]
    public async Task Update_InApproval_Returns409()
    {
        var (submitted, etag) = await SubmittedRequestAsync(Employee1);
        using var client = ClientFor(Employee1);

        using var response = await UpdateAsync(client, submitted.Id, etag, ValidBody);

        Assert.That(response.StatusCode, Is.EqualTo(HttpStatusCode.Conflict));
        Assert.That((await ReadProblemAsync(response)).Code, Is.EqualTo("request.invalid_transition"));
    }

    [Test]
    public async Task Update_ByRequestersManager_Returns403()
    {
        var (created, etag) = await CreateRequestAsync(Employee1);
        using var client = ClientFor(Manager1);

        using var response = await UpdateAsync(client, created.Id, etag, ValidBody);

        Assert.That(response.StatusCode, Is.EqualTo(HttpStatusCode.Forbidden));
        Assert.That((await ReadProblemAsync(response)).Code, Is.EqualTo("request.not_owner"));
    }

    [Test]
    public async Task Update_WithStaleETag_Returns412()
    {
        var (created, staleETag) = await CreateRequestAsync(Employee1);
        using var client = ClientFor(Employee1);
        using var first = await UpdateAsync(client, created.Id, staleETag, ValidBody);

        using var second = await UpdateAsync(client, created.Id, staleETag, new { title = "Lost update", category = "Expense", amount = 1m });

        Assert.That(second.StatusCode, Is.EqualTo(HttpStatusCode.PreconditionFailed));
        var (current, _) = await GetRequestAsync(Employee1, created.Id);
        Assert.That(current.Title, Is.EqualTo("Updated title"), "the first update was not overwritten");
    }

    [Test]
    public async Task Update_WithInvalidBody_Returns400()
    {
        var (created, etag) = await CreateRequestAsync(Employee1);
        using var client = ClientFor(Employee1);

        using var response = await UpdateAsync(client, created.Id, etag, new { title = "x", category = "Expense", amount = -5 });

        Assert.That(response.StatusCode, Is.EqualTo(HttpStatusCode.BadRequest));
        Assert.That((await ReadProblemAsync(response)).Errors!.Keys, Is.EquivalentTo(new[] { "title", "amount" }));
    }
}
