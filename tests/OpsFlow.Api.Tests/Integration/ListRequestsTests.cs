using System.Net;
using OpsFlow.Api.Features.Common;
using OpsFlow.Api.Features.Requests;
using OpsFlow.Domain.Requests;
using static OpsFlow.Api.Tests.Integration.TestUsers;

namespace OpsFlow.Api.Tests.Integration;

[TestFixture]
public class ListRequestsTests : IntegrationTestBase
{
    [SetUp]
    public async Task CreateRequestsAsync()
    {
        await CreateRequestAsync(Employee1, 100m, "Keyboard");
        await CreateRequestAsync(Employee1, 2_500m, "Laptop for design");
        await SubmittedRequestAsync(Employee2, 9_000m);              // "New laptop", in approval
        await CreateRequestAsync(Employee3, 40m, "Taxi to airport");
    }

    // BR-40 / BR-41 applied to lists.
    [TestCase("Employee1", 2)]
    [TestCase("Employee2", 1)]
    [TestCase("Manager1", 3)]
    [TestCase("Manager2", 1)]
    [TestCase("Finance1", 4)]
    [TestCase("Admin", 4)]
    public async Task List_ReturnsOnlyVisibleRequests(string viewer, int expected)
    {
        var page = await ListAsync(ByName(viewer), "");

        Assert.That(page.TotalCount, Is.EqualTo(expected));
    }

    [Test]
    public async Task List_DefaultSort_IsNewestFirst()
    {
        var page = await ListAsync(Admin, "");

        Assert.That(page.Items.Select(i => i.CreatedAt), Is.Ordered.Descending);
    }

    [TestCase("status=InApproval", 1)]
    [TestCase("category=Expense", 0)]
    [TestCase("minAmount=100&maxAmount=2500", 2)]
    [TestCase("q=laptop", 2)]
    [TestCase("q=REQ-", 4)]
    [TestCase("createdFrom=2000-01-01&createdTo=2000-12-31", 0)]
    public async Task List_AppliesFilters(string query, int expected)
    {
        var page = await ListAsync(Admin, query);

        Assert.That(page.TotalCount, Is.EqualTo(expected), string.Join(", ", page.Items.Select(i => $"{i.Title} {i.Amount} {i.Status}")));
    }

    [Test]
    public async Task List_FilterByDateRangeInUtc_IncludesRequestsCreatedInIt()
    {
        // A range around today, so the test cannot fail when it runs across midnight UTC.
        var utcToday = DateOnly.FromDateTime(DateTime.UtcNow);
        var from = utcToday.AddDays(-1).ToString("yyyy-MM-dd", System.Globalization.CultureInfo.InvariantCulture);
        var to = utcToday.AddDays(1).ToString("yyyy-MM-dd", System.Globalization.CultureInfo.InvariantCulture);

        var page = await ListAsync(Admin, $"createdFrom={from}&createdTo={to}");

        Assert.That(page.TotalCount, Is.EqualTo(4));
    }

    [TestCase("amount", false)]
    [TestCase("-amount", true)]
    public async Task List_SortsByAmount(string sort, bool descending)
    {
        var page = await ListAsync(Admin, $"sort={sort}");

        var amounts = page.Items.Select(i => i.Amount).ToList();
        Assert.That(amounts, descending ? Is.Ordered.Descending : Is.Ordered.Ascending);
    }

    [Test]
    public async Task List_PagesWithStableTotals()
    {
        var first = await ListAsync(Admin, "pageSize=3&page=1&sort=reference");
        var second = await ListAsync(Admin, "pageSize=3&page=2&sort=reference");

        Assert.Multiple(() =>
        {
            Assert.That(first.TotalCount, Is.EqualTo(4));
            Assert.That(first.TotalPages, Is.EqualTo(2));
            Assert.That(first.Items, Has.Count.EqualTo(3));
            Assert.That(second.Items, Has.Count.EqualTo(1));
            Assert.That(first.Items.Select(i => i.Id).Intersect(second.Items.Select(i => i.Id)), Is.Empty);
        });
    }

    [TestCase("sort=title", "sort")]
    [TestCase("pageSize=500", "pageSize")]
    [TestCase("minAmount=10&maxAmount=5", "maxAmount")]
    [TestCase("createdFrom=2026-02-01&createdTo=2026-01-01", "createdTo")]
    [TestCase("createdTo=9999-12-31", "createdTo")]
    [TestCase("page=100001", "page")]
    [TestCase("page=2147483647&pageSize=100", "page")]
    public async Task List_WithInvalidQuery_Returns400NamingTheField(string query, string field)
    {
        using var client = ClientFor(Admin);

        using var response = await client.GetAsync(new Uri($"/api/v1/requests?{query}", UriKind.Relative));

        Assert.That(response.StatusCode, Is.EqualTo(HttpStatusCode.BadRequest));
        Assert.That((await ReadProblemAsync(response)).Errors!.Keys, Does.Contain(field));
    }

    [Test]
    public async Task List_WithUnparseableValue_Returns400()
    {
        using var client = ClientFor(Admin);

        using var response = await client.GetAsync(new Uri("/api/v1/requests?status=Nope", UriKind.Relative));

        Assert.That(response.StatusCode, Is.EqualTo(HttpStatusCode.BadRequest));
    }

    private static async Task<PagedResponse<RequestSummaryResponse>> ListAsync(TestUser user, string query)
    {
        using var client = ClientFor(user);
        using var response = await client.GetAsync(new Uri($"/api/v1/requests?{query}", UriKind.Relative));
        Assert.That(response.StatusCode, Is.EqualTo(HttpStatusCode.OK), await response.Content.ReadAsStringAsync());
        return await ReadAsync<PagedResponse<RequestSummaryResponse>>(response);
    }

    private static TestUser ByName(string name) => (TestUser)typeof(TestUsers).GetField(name)!.GetValue(null)!;
}
