using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text;
using Microsoft.IdentityModel.JsonWebTokens;
using Microsoft.IdentityModel.Tokens;
using OpsFlow.Api.Features.Auth;
using OpsFlow.Api.Features.Profile;
using OpsFlow.Domain.Users;
using static OpsFlow.Api.Tests.Integration.TestUsers;

namespace OpsFlow.Api.Tests.Integration;

[TestFixture]
public class AuthTests : IntegrationTestBase
{
    private const string TokenUrl = "/api/v1/auth/token";

    [Test]
    public async Task Token_WithValidCredentials_ReturnsTokenThatAuthenticates()
    {
        using var anonymous = AnonymousClient();

        using var response = await anonymous.PostAsJsonAsync(TokenUrl, new { email = Employee1.Email.ToUpperInvariant(), password = Password });

        Assert.That(response.StatusCode, Is.EqualTo(HttpStatusCode.OK));
        var token = await ReadAsync<TokenResponse>(response);
        Assert.Multiple(() =>
        {
            Assert.That(token.TokenType, Is.EqualTo("Bearer"));
            Assert.That(token.ExpiresAt, Is.EqualTo(DateTimeOffset.UtcNow.AddMinutes(15)).Within(TimeSpan.FromMinutes(1)));
        });

        using var authenticated = AnonymousClient();
        authenticated.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", token.AccessToken);
        var me = await authenticated.GetFromJsonAsync<MeResponse>("/api/v1/me", Json);
        Assert.That(me, Is.EqualTo(new MeResponse(Employee1.Id, Employee1.Email, Employee1.DisplayName, UserRole.Employee, Manager1.Id)));
    }

    [Test]
    public async Task Token_WithWrongPassword_AndWithUnknownEmail_ReturnIdenticalProblems()
    {
        using var client = AnonymousClient();

        using var wrongPassword = await client.PostAsJsonAsync(TokenUrl, new { email = Employee1.Email, password = "wrong-password" });
        using var unknownEmail = await client.PostAsJsonAsync(TokenUrl, new { email = "nobody@tests.local", password = Password });

        var first = await ReadProblemAsync(wrongPassword);
        var second = await ReadProblemAsync(unknownEmail);
        Assert.Multiple(() =>
        {
            Assert.That(wrongPassword.StatusCode, Is.EqualTo(HttpStatusCode.Unauthorized));
            Assert.That(unknownEmail.StatusCode, Is.EqualTo(HttpStatusCode.Unauthorized));
            Assert.That(first.Code, Is.EqualTo("auth.invalid_credentials"));
            Assert.That(second with { CorrelationId = null }, Is.EqualTo(first with { CorrelationId = null }));
        });
    }

    [Test]
    public async Task Token_ForInactiveUser_IsRejected()
    {
        using var client = AnonymousClient();

        using var response = await client.PostAsJsonAsync(TokenUrl, new { email = InactiveEmployee.Email, password = Password });

        Assert.That(response.StatusCode, Is.EqualTo(HttpStatusCode.Unauthorized));
        Assert.That((await ReadProblemAsync(response)).Code, Is.EqualTo("auth.invalid_credentials"));
    }

    [Test]
    public async Task Token_WithMissingFields_Returns400WithFieldErrors()
    {
        using var client = AnonymousClient();

        using var response = await client.PostAsJsonAsync(TokenUrl, new { email = "" });

        Assert.That(response.StatusCode, Is.EqualTo(HttpStatusCode.BadRequest));
        var problem = await ReadProblemAsync(response);
        Assert.Multiple(() =>
        {
            Assert.That(problem.Code, Is.EqualTo("validation.failed"));
            Assert.That(problem.Errors!.Keys, Is.EquivalentTo(new[] { "email", "password" }));
        });
    }

    [Test]
    public async Task ProtectedEndpoint_WithoutToken_Returns401Problem()
    {
        using var client = AnonymousClient();

        using var response = await client.GetAsync(new Uri("/api/v1/me", UriKind.Relative));

        Assert.That(response.StatusCode, Is.EqualTo(HttpStatusCode.Unauthorized));
        Assert.That((await ReadProblemAsync(response)).Code, Is.EqualTo("auth.unauthenticated"));
    }

    [Test]
    public async Task ProtectedEndpoint_WithTokenSignedByAnotherKey_Returns401()
    {
        var forged = new JsonWebTokenHandler().CreateToken(new SecurityTokenDescriptor
        {
            Issuer = "opsflow",
            Audience = "opsflow-api",
            Expires = DateTime.UtcNow.AddMinutes(5),
            Claims = new Dictionary<string, object>
            {
                [JwtRegisteredClaimNames.Sub] = Admin.Id.ToString(System.Globalization.CultureInfo.InvariantCulture),
                ["role"] = "Admin",
            },
            SigningCredentials = new SigningCredentials(
                new SymmetricSecurityKey(Encoding.UTF8.GetBytes("an-attacker-chosen-key-that-is-long-enough!!")),
                SecurityAlgorithms.HmacSha256),
        });
        using var client = AnonymousClient();
        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", forged);

        using var response = await client.GetAsync(new Uri("/api/v1/me", UriKind.Relative));

        Assert.That(response.StatusCode, Is.EqualTo(HttpStatusCode.Unauthorized));
    }

    [Test]
    public async Task Token_RateLimit_IsPerClientIp_WhenForwardedHeadersAreEnabled()
    {
        await using var factory = new OpsFlowApiFactory(
            IntegrationTestEnvironment.ConnectionString,
            new Dictionary<string, string?> { ["RateLimiting:TokenPermitLimit"] = "2", ["ForwardedHeaders:Enabled"] = "true" });
        using var client = factory.CreateClient();

        async Task<HttpStatusCode> AttemptFromAsync(string ip)
        {
            using var message = new HttpRequestMessage(HttpMethod.Post, TokenUrl)
            {
                Content = JsonContent.Create(new { email = Employee1.Email, password = "wrong-password" }),
            };
            message.Headers.Add("X-Forwarded-For", ip);
            using var response = await client.SendAsync(message);
            return response.StatusCode;
        }

        var clientA = new[] { await AttemptFromAsync("203.0.113.10"), await AttemptFromAsync("203.0.113.10"), await AttemptFromAsync("203.0.113.10") };
        var clientB = await AttemptFromAsync("198.51.100.20");

        Assert.Multiple(() =>
        {
            Assert.That(clientA, Is.EqualTo(new[] { HttpStatusCode.Unauthorized, HttpStatusCode.Unauthorized, HttpStatusCode.TooManyRequests }));
            Assert.That(clientB, Is.EqualTo(HttpStatusCode.Unauthorized), "another client behind the same proxy is not throttled");
        });
    }

    [Test]
    public async Task Token_SixthAttemptWithinAMinute_Returns429WithRetryAfter()
    {
        // A dedicated host with the production limit, so other tests do not share its window.
        await using var factory = new OpsFlowApiFactory(
            IntegrationTestEnvironment.ConnectionString,
            new Dictionary<string, string?> { ["RateLimiting:TokenPermitLimit"] = "5" });
        using var client = factory.CreateClient();

        for (var attempt = 1; attempt <= 5; attempt++)
        {
            using var allowed = await client.PostAsJsonAsync(TokenUrl, new { email = Employee1.Email, password = "wrong-password" });
            Assert.That(allowed.StatusCode, Is.EqualTo(HttpStatusCode.Unauthorized), $"attempt {attempt}");
        }

        using var limited = await client.PostAsJsonAsync(TokenUrl, new { email = Employee1.Email, password = Password });

        Assert.That(limited.StatusCode, Is.EqualTo(HttpStatusCode.TooManyRequests));
        Assert.Multiple(async () =>
        {
            Assert.That(limited.Headers.RetryAfter, Is.Not.Null);
            Assert.That((await ReadProblemAsync(limited)).Code, Is.EqualTo("rate_limit.exceeded"));
        });
    }
}
