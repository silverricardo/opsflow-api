using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Diagnostics;
using Microsoft.Extensions.DependencyInjection;
using OpsFlow.Api.Infrastructure.Auth;
using static OpsFlow.Api.Tests.Integration.TestUsers;

namespace OpsFlow.Api.Tests.Integration;

/// <summary>
/// Found in the Phase 7 review: if the response failed after the command had committed, the filter
/// released the key and a retry executed the command again (a duplicate request). Now the key is kept.
/// </summary>
[TestFixture]
public class IdempotencyAfterCommitTests : IntegrationTestBase
{
    [Test]
    public async Task FailureAfterCommit_KeepsTheKey_SoARetryCannotCreateADuplicate()
    {
        await using var factory = new OpsFlowApiFactory(
            IntegrationTestEnvironment.ConnectionString,
            configureServices: services =>
            {
                services.AddHttpContextAccessor();
                services.AddSingleton<IInterceptor, FailAfterCommitInterceptor>();
            });
        using var client = factory.CreateClient();
        var token = factory.Services.GetRequiredService<TokenIssuer>().Issue(Employee1.Id, Employee1.Email, Employee1.Role).AccessToken;
        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", token);
        var body = new { title = "Saved but unanswered", category = "Purchase", amount = 10m };

        using var first = new HttpRequestMessage(HttpMethod.Post, "/api/v1/requests") { Content = JsonContent.Create(body) };
        first.Headers.Add("Idempotency-Key", "after-commit-1");
        first.Headers.Add(FailAfterCommitInterceptor.HeaderName, "1");
        using var failed = await client.SendAsync(first);

        using var second = new HttpRequestMessage(HttpMethod.Post, "/api/v1/requests") { Content = JsonContent.Create(body) };
        second.Headers.Add("Idempotency-Key", "after-commit-1");
        using var retry = await client.SendAsync(second);

        Assert.Multiple(async () =>
        {
            Assert.That(failed.StatusCode, Is.EqualTo(HttpStatusCode.InternalServerError), "the response failed after the save");
            Assert.That(retry.StatusCode, Is.EqualTo(HttpStatusCode.Conflict));
            Assert.That((await ReadProblemAsync(retry)).Code, Is.EqualTo("idempotency.in_progress"));
            Assert.That(await QueryDbAsync(db => db.Requests.CountAsync()), Is.EqualTo(1), "the command ran exactly once");
        });
    }
}
