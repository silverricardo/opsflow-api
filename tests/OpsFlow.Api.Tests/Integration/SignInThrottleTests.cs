using System.Net;
using System.Net.Http.Json;
using static OpsFlow.Api.Tests.Integration.TestUsers;

namespace OpsFlow.Api.Tests.Integration;

[TestFixture]
public class SignInThrottleTests : IntegrationTestBase
{
    private const string TokenUrl = "/api/v1/auth/token";

    [Test]
    public async Task TooManyFailuresForOneAccount_BlockThatAccountOnly_EvenWithTheRightPassword()
    {
        // Own host (own memory cache), with the per-IP limit lifted so only the per-account limit applies.
        await using var factory = new OpsFlowApiFactory(
            IntegrationTestEnvironment.ConnectionString,
            new Dictionary<string, string?> { ["SignInThrottle:MaxFailures"] = "3" });
        using var client = factory.CreateClient();

        for (var attempt = 1; attempt <= 3; attempt++)
        {
            using var wrong = await client.PostAsJsonAsync(TokenUrl, new { email = Employee2.Email, password = "wrong-password" });
            Assert.That(wrong.StatusCode, Is.EqualTo(HttpStatusCode.Unauthorized), $"attempt {attempt}");
        }

        using var blocked = await client.PostAsJsonAsync(TokenUrl, new { email = Employee2.Email, password = Password });
        using var otherAccount = await client.PostAsJsonAsync(TokenUrl, new { email = Employee3.Email, password = Password });

        Assert.Multiple(async () =>
        {
            Assert.That(blocked.StatusCode, Is.EqualTo(HttpStatusCode.TooManyRequests));
            Assert.That((await ReadProblemAsync(blocked)).Code, Is.EqualTo("rate_limit.exceeded"));
            Assert.That(otherAccount.StatusCode, Is.EqualTo(HttpStatusCode.OK));
        });
    }

    [Test]
    public async Task UnknownEmails_AreThrottledTheSameWay_SoTheLimitRevealsNothing()
    {
        await using var factory = new OpsFlowApiFactory(
            IntegrationTestEnvironment.ConnectionString,
            new Dictionary<string, string?> { ["SignInThrottle:MaxFailures"] = "2" });
        using var client = factory.CreateClient();
        var unknown = new { email = "nobody-here@tests.local", password = "whatever-password" };

        using var first = await client.PostAsJsonAsync(TokenUrl, unknown);
        using var second = await client.PostAsJsonAsync(TokenUrl, unknown);
        using var third = await client.PostAsJsonAsync(TokenUrl, unknown);

        Assert.That(new[] { first.StatusCode, second.StatusCode, third.StatusCode },
            Is.EqualTo(new[] { HttpStatusCode.Unauthorized, HttpStatusCode.Unauthorized, HttpStatusCode.TooManyRequests }));
    }

    [Test]
    public async Task SuccessfulSignIn_ResetsTheCount()
    {
        await using var factory = new OpsFlowApiFactory(
            IntegrationTestEnvironment.ConnectionString,
            new Dictionary<string, string?> { ["SignInThrottle:MaxFailures"] = "2" });
        using var client = factory.CreateClient();

        using var wrong1 = await client.PostAsJsonAsync(TokenUrl, new { email = Employee3.Email, password = "wrong-password" });
        using var right = await client.PostAsJsonAsync(TokenUrl, new { email = Employee3.Email, password = Password });
        using var wrong2 = await client.PostAsJsonAsync(TokenUrl, new { email = Employee3.Email, password = "wrong-password" });
        using var rightAgain = await client.PostAsJsonAsync(TokenUrl, new { email = Employee3.Email, password = Password });

        Assert.That(rightAgain.StatusCode, Is.EqualTo(HttpStatusCode.OK), "one failure after a reset is below the limit of 2");
    }
}
