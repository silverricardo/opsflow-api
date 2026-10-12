using System.Net;
using OpsFlow.Api.Features.Requests;
using static OpsFlow.Api.Tests.Integration.TestUsers;

namespace OpsFlow.Api.Tests.Integration;

[TestFixture]
public class GetRequestTests : IntegrationTestBase
{
    [Test]
    public async Task Get_AsRequester_ReturnsDetailsWithCurrentETag()
    {
        var (created, etag) = await CreateRequestAsync(Employee1);
        using var client = ClientFor(Employee1);

        using var response = await client.GetAsync(new Uri($"/api/v1/requests/{created.Id}", UriKind.Relative));

        Assert.That(response.StatusCode, Is.EqualTo(HttpStatusCode.OK));
        Assert.Multiple(async () =>
        {
            Assert.That(response.Headers.ETag?.Tag, Is.EqualTo(etag));
            Assert.That((await ReadAsync<RequestDetailsResponse>(response)).Id, Is.EqualTo(created.Id));
            Assert.That(response.Headers.CacheControl?.NoStore, Is.True);
        });
    }

    // BR-40 / BR-41: visibility by relationship or role.
    [TestCase("Manager1", TestName = "requester's manager")]
    [TestCase("Finance1", TestName = "finance")]
    [TestCase("Director", TestName = "director")]
    [TestCase("Admin", TestName = "admin")]
    public async Task Get_AsUserAllowedToSeeIt_Returns200(string viewer)
    {
        var (created, _) = await CreateRequestAsync(Employee1);
        using var client = ClientFor(ByName(viewer));

        using var response = await client.GetAsync(new Uri($"/api/v1/requests/{created.Id}", UriKind.Relative));

        Assert.That(response.StatusCode, Is.EqualTo(HttpStatusCode.OK));
    }

    // BR-42: invisible requests are indistinguishable from missing ones.
    [TestCase("Employee2", TestName = "colleague with same manager")]
    [TestCase("Manager2", TestName = "another team's manager")]
    public async Task Get_AsUserNotAllowedToSeeIt_Returns404(string viewer)
    {
        var (created, _) = await CreateRequestAsync(Employee1);
        using var client = ClientFor(ByName(viewer));

        using var response = await client.GetAsync(new Uri($"/api/v1/requests/{created.Id}", UriKind.Relative));

        Assert.That(response.StatusCode, Is.EqualTo(HttpStatusCode.NotFound));
        Assert.That((await ReadProblemAsync(response)).Code, Is.EqualTo("request.not_found"));
    }

    [Test]
    public async Task Get_NonExistentRequest_Returns404()
    {
        using var client = ClientFor(Admin);

        using var response = await client.GetAsync(new Uri("/api/v1/requests/987654321", UriKind.Relative));

        Assert.That(response.StatusCode, Is.EqualTo(HttpStatusCode.NotFound));
    }

    private static TestUser ByName(string name) =>
        (TestUser)typeof(TestUsers).GetField(name)!.GetValue(null)!;
}
