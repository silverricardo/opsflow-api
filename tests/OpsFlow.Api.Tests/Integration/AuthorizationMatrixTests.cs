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

    private static readonly (string Method, string Path, object? Body, UserRole[] Allowed)[] Endpoints =
    [
        ("GET", "/api/v1/me", null, AllRoles()),
        ("GET", "/api/v1/requests", null, AllRoles()),
        ("POST", "/api/v1/requests", new { }, AllRoles()), // Admin passes the policy; the domain rejects it (covered elsewhere)
        ("GET", Missing, null, AllRoles()),
        ("PUT", Missing, new { title = "Valid", category = "Purchase", amount = 1 }, AllRoles()),
        ("POST", $"{Missing}/submit", null, AllRoles()),
        ("POST", $"{Missing}/cancel", null, AllRoles()),
        ("POST", $"{Missing}/steps/1/decision", new { decision = "Approve" }, AllRoles()),
        ("GET", $"{Missing}/audit", null, AllRoles()),
        ("POST", $"{Missing}/steps/1/claim", null, [UserRole.Manager, UserRole.Finance, UserRole.Director]),
        ("PUT", $"{Missing}/steps/1/assignee", new { userId = 1 }, [UserRole.Admin]),
        ("POST", $"{Missing}/fulfil", null, [UserRole.Finance]),
        ("GET", "/api/v1/approvals/inbox", null, [UserRole.Manager, UserRole.Finance, UserRole.Director]),
        ("GET", "/api/v1/users", null, [UserRole.Admin]),
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
        select new TestCaseData(endpoint.Method, endpoint.Path, endpoint.Body, role, allowed)
            .SetName($"{endpoint.Method} {endpoint.Path.Replace("999999", "{id}", StringComparison.Ordinal)} as {role} → {(allowed ? "allowed" : "403")}");

    [TestCaseSource(nameof(Matrix))]
    public async Task Endpoint_EnforcesRolePolicy(string method, string path, object? body, UserRole role, bool allowed)
    {
        using var client = ClientFor(UserForRole[role]);

        using var response = await SendCommandAsync(client, new HttpMethod(method), path, etag: "\"AAAAAAAAAAA=\"", body: body);

        if (allowed)
        {
            Assert.That(response.StatusCode, Is.Not.EqualTo(HttpStatusCode.Forbidden).And.Not.EqualTo(HttpStatusCode.Unauthorized),
                await response.Content.ReadAsStringAsync());
        }
        else
        {
            Assert.That(response.StatusCode, Is.EqualTo(HttpStatusCode.Forbidden));
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
}
