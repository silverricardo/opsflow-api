using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text.Json;
using System.Text.Json.Serialization;
using Microsoft.Extensions.DependencyInjection;
using OpsFlow.Api.Features.Requests;
using OpsFlow.Api.Infrastructure.Auth;
using OpsFlow.Api.Persistence;

namespace OpsFlow.Api.Tests.Integration;

public sealed record ProblemBody(
    int Status,
    string Code,
    string? Title,
    string? Detail,
    string? Instance,
    string? CorrelationId,
    Dictionary<string, string[]>? Errors);

public abstract class IntegrationTestBase
{
    protected static readonly JsonSerializerOptions Json = new(JsonSerializerDefaults.Web)
    {
        Converters = { new JsonStringEnumConverter() },
    };

    [SetUp]
    public Task ResetDataAsync() => IntegrationTestEnvironment.ResetDataAsync();

    protected static HttpClient AnonymousClient() => IntegrationTestEnvironment.Factory.CreateClient();

    /// <summary>A client authenticated as <paramref name="user"/>, with a token from the API's own issuer.</summary>
    private protected static HttpClient ClientFor(TestUser user)
    {
        var issuer = IntegrationTestEnvironment.Factory.Services.GetRequiredService<TokenIssuer>();
        var client = IntegrationTestEnvironment.Factory.CreateClient();
        client.DefaultRequestHeaders.Authorization =
            new AuthenticationHeaderValue("Bearer", issuer.Issue(user.Id, user.Email, user.Role).AccessToken);
        return client;
    }

    private protected static async Task<(RequestDetailsResponse Request, string ETag)> CreateRequestAsync(
        TestUser requester,
        decimal amount = 1_200m,
        string title = "New laptop")
    {
        using var client = ClientFor(requester);
        using var response = await client.PostAsJsonAsync(
            "/api/v1/requests",
            new { title, description = "Created by a test", category = "Purchase", amount });

        Assert.That(response.StatusCode, Is.EqualTo(System.Net.HttpStatusCode.Created), await response.Content.ReadAsStringAsync());
        return (await ReadAsync<RequestDetailsResponse>(response), response.Headers.ETag!.Tag);
    }

    protected static async Task<HttpResponseMessage> SubmitAsync(HttpClient client, long requestId, string? etag)
    {
        ArgumentNullException.ThrowIfNull(client);
        using var message = new HttpRequestMessage(HttpMethod.Post, $"/api/v1/requests/{requestId}/submit");
        if (etag is not null)
        {
            message.Headers.TryAddWithoutValidation("If-Match", etag);
        }

        return await client.SendAsync(message);
    }

    protected static async Task<HttpResponseMessage> DecideAsync(
        HttpClient client, long requestId, long stepId, string? etag, string decision, string? comment = null)
    {
        ArgumentNullException.ThrowIfNull(client);
        using var message = new HttpRequestMessage(HttpMethod.Post, $"/api/v1/requests/{requestId}/steps/{stepId}/decision")
        {
            Content = JsonContent.Create(new { decision, comment }),
        };
        if (etag is not null)
        {
            message.Headers.TryAddWithoutValidation("If-Match", etag);
        }

        return await client.SendAsync(message);
    }

    protected static async Task<HttpResponseMessage> UpdateAsync(HttpClient client, long requestId, string? etag, object body)
    {
        ArgumentNullException.ThrowIfNull(client);
        using var message = new HttpRequestMessage(HttpMethod.Put, $"/api/v1/requests/{requestId}") { Content = JsonContent.Create(body) };
        if (etag is not null)
        {
            message.Headers.TryAddWithoutValidation("If-Match", etag);
        }

        return await client.SendAsync(message);
    }

    /// <summary>Reads the request as <paramref name="user"/> and returns it with its current ETag.</summary>
    private protected static async Task<(RequestDetailsResponse Request, string ETag)> GetRequestAsync(TestUser user, long requestId)
    {
        using var client = ClientFor(user);
        using var response = await client.GetAsync(new Uri($"/api/v1/requests/{requestId}", UriKind.Relative));
        Assert.That(response.StatusCode, Is.EqualTo(System.Net.HttpStatusCode.OK), await response.Content.ReadAsStringAsync());
        return (await ReadAsync<RequestDetailsResponse>(response), response.Headers.ETag!.Tag);
    }

    /// <summary>Creates and submits a request; returns it in approval with its ETag.</summary>
    private protected static async Task<(RequestDetailsResponse Request, string ETag)> SubmittedRequestAsync(
        TestUser requester, decimal amount = 1_200m)
    {
        var (created, etag) = await CreateRequestAsync(requester, amount);
        using var client = ClientFor(requester);
        using var response = await SubmitAsync(client, created.Id, etag);
        Assert.That(response.StatusCode, Is.EqualTo(System.Net.HttpStatusCode.OK), await response.Content.ReadAsStringAsync());
        return (await ReadAsync<RequestDetailsResponse>(response), response.Headers.ETag!.Tag);
    }

    /// <summary>Decides the request's current step as <paramref name="decider"/>, asserting success.</summary>
    private protected static async Task<(RequestDetailsResponse Request, string ETag)> DecideCurrentStepAsync(
        TestUser decider, long requestId, string decision = "Approve", string? comment = null)
    {
        var (request, etag) = await GetRequestAsync(decider, requestId);
        var current = request.Steps.First(s => s.Status == OpsFlow.Domain.Requests.StepStatus.Pending);
        using var client = ClientFor(decider);
        using var response = await DecideAsync(client, requestId, current.Id, etag, decision, comment);
        Assert.That(response.StatusCode, Is.EqualTo(System.Net.HttpStatusCode.OK), await response.Content.ReadAsStringAsync());
        return (await ReadAsync<RequestDetailsResponse>(response), response.Headers.ETag!.Tag);
    }

    protected static async Task<T> ReadAsync<T>(HttpResponseMessage response)
    {
        ArgumentNullException.ThrowIfNull(response);
        var body = await response.Content.ReadFromJsonAsync<T>(Json);
        Assert.That(body, Is.Not.Null);
        return body!;
    }

    protected static Task<ProblemBody> ReadProblemAsync(HttpResponseMessage response)
    {
        ArgumentNullException.ThrowIfNull(response);
        Assert.That(response.Content.Headers.ContentType?.MediaType, Is.EqualTo("application/problem+json"));
        return ReadAsync<ProblemBody>(response);
    }

    private protected static async Task<T> QueryDbAsync<T>(Func<OpsFlowDbContext, Task<T>> query)
    {
        ArgumentNullException.ThrowIfNull(query);
        await using var scope = IntegrationTestEnvironment.Factory.Services.CreateAsyncScope();
        return await query(scope.ServiceProvider.GetRequiredService<OpsFlowDbContext>());
    }
}
