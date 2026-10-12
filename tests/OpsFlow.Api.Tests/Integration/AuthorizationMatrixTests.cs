using System.Net;
using OpsFlow.Domain.Users;
using static OpsFlow.Api.Tests.Integration.TestUsers;

namespace OpsFlow.Api.Tests.Integration;

/// <summary>
/// AC-16: role × endpoint. Each endpoint is called with a request ID that does not exist: roles the
/// policy allows get past authorization (404 / 428 / 400), roles it forbids get 403 from the policy.
/// </summary>
[TestFixture]
public class AuthorizationMatrixTests : IntegrationTestBase
{
    private const string Missing = "/api/v1/requests/999999";

    // Allowed roles must get exactly the expected status (not merely "not 401/403", which a 500 would pass).
    private static readonly (string Method, string Path, object? Body, UserRole[] Allowed, HttpStatusCode Expected)[] Endpoints =
    [
        ("GET", "/api/v1/me", null, AllRoles(), HttpStatusCode.OK),
        ("GET", "/api/v1/requests", null, AllRoles(), HttpStatusCode.OK),
        ("POST", "/api/v1/requests", new { }, AllRoles(), HttpStatusCode.BadRequest), // Admin passes the policy; the domain rejects Admin (covered elsewhere)
        ("GET", Missing, null, AllRoles(), HttpStatusCode.NotFound),
        ("PUT", Missing, new { title = "Valid", category = "Purchase", amount = 1 }, AllRoles(), HttpStatusCode.NotFound),
        ("POST", $"{Missing}/submit", null, AllRoles(), HttpStatusCode.NotFound),
        ("POST", $"{Missing}/cancel", null, AllRoles(), HttpStatusCode.NotFound),
        ("GET", $"{Missing}/audit", null, AllRoles(), HttpStatusCode.NotFound),
        ("POST", $"{Missing}/steps/1/decision", new { decision = "Approve" }, Approvers(), HttpStatusCode.NotFound),
        ("POST", $"{Missing}/steps/1/claim", null, Approvers(), HttpStatusCode.NotFound),
        ("PUT", $"{Missing}/steps/1/assignee", new { userId = 1 }, [UserRole.Admin], HttpStatusCode.NotFound),
        ("POST", $"{Missing}/fulfil", null, [UserRole.Finance], HttpStatusCode.NotFound),
        ("GET", "/api/v1/approvals/inbox", null, Approvers(), HttpStatusCode.OK),
        ("GET", "/api/v1/users", null, [UserRole.Admin], HttpStatusCode.OK),
    ];

    private static readonly Dictionary<UserRole, TestUser> UserForRole = new()
    {
        [UserRole.Employee] = Employee1,
        [UserRole.Manager] = Manager1,
        [UserRole.Finance] = Finance1,
        [UserRole.Director] = Director,
        [UserRole.Admin] = Admin,
    };

    private static IEnumerable<TestCaseData> Matrix() =>
        from endpoint in Endpoints
        from role in Enum.GetValues<UserRole>()
        let allowed = endpoint.Allowed.Contains(role)
        let expected = allowed ? endpoint.Expected : HttpStatusCode.Forbidden
        select new TestCaseData(endpoint.Method, endpoint.Path, endpoint.Body, role, expected)
            .SetName($"{endpoint.Method} {endpoint.Path.Replace("999999", "{id}", StringComparison.Ordinal)} as {role} → {(int)expected}");

    [TestCaseSource(nameof(Matrix))]
    public async Task Endpoint_EnforcesRolePolicy(string method, string path, object? body, UserRole role, HttpStatusCode expected)
    {
        using var client = ClientFor(UserForRole[role]);

        using var response = await SendCommandAsync(client, new HttpMethod(method), path, etag: "\"AAAAAAAAAAA=\"", body: body);

        Assert.That(response.StatusCode, Is.EqualTo(expected), await response.Content.ReadAsStringAsync());
        if (expected == HttpStatusCode.Forbidden)
        {
            Assert.That((await ReadProblemAsync(response)).Code, Is.EqualTo("auth.forbidden"));
        }
    }

    [TestCaseSource(nameof(AnonymousCases))]
    public async Task Endpoint_WithoutToken_Returns401(string method, string path)
    {
        using var client = AnonymousClient();

        using var response = await SendCommandAsync(client, new HttpMethod(method), path);

        Assert.That(response.StatusCode, Is.EqualTo(HttpStatusCode.Unauthorized));
    }

    private static IEnumerable<TestCaseData> AnonymousCases() =>
        Endpoints.Select(e => new TestCaseData(e.Method, e.Path).SetName($"{e.Method} {e.Path.Replace("999999", "{id}", StringComparison.Ordinal)} anonymous → 401"));

    private static UserRole[] AllRoles() => Enum.GetValues<UserRole>();

    private static UserRole[] Approvers() => [UserRole.Manager, UserRole.Finance, UserRole.Director];
}
