using System.Net;
using OpsFlow.Api.Features.Requests;
using OpsFlow.Domain.Requests;
using static OpsFlow.Api.Tests.Integration.TestUsers;

namespace OpsFlow.Api.Tests.Integration;

[TestFixture]
public class StepAssignmentTests : IntegrationTestBase
{
    // AC-17: claim, then reassign, and the previous assignee loses the step.
    [Test]
    public async Task Claim_ThenReassign_MovesTheStepAndTheFormerAssigneeCannotDecide()
    {
        var (submitted, _) = await SubmittedRequestAsync(Employee3, 7_000m);
        var (afterManager, etag) = await DecideCurrentStepAsync(Manager2, submitted.Id);
        var financeStep = afterManager.Steps[1];
        using var finance1 = ClientFor(Finance1);

        using var claimed = await SendCommandAsync(finance1, HttpMethod.Post, $"/api/v1/requests/{submitted.Id}/steps/{financeStep.Id}/claim", etag);
        Assert.That(claimed.StatusCode, Is.EqualTo(HttpStatusCode.OK), await claimed.Content.ReadAsStringAsync());
        Assert.That((await ReadAsync<RequestDetailsResponse>(claimed)).Steps[1].Assignee?.Id, Is.EqualTo(Finance1.Id));

        using var admin = ClientFor(Admin);
        using var reassigned = await SendCommandAsync(admin, HttpMethod.Put,
            $"/api/v1/requests/{submitted.Id}/steps/{financeStep.Id}/assignee", claimed.Headers.ETag!.Tag, new { userId = Finance2.Id });
        Assert.That(reassigned.StatusCode, Is.EqualTo(HttpStatusCode.OK), await reassigned.Content.ReadAsStringAsync());

        using var formerAssignee = await DecideAsync(finance1, submitted.Id, financeStep.Id, reassigned.Headers.ETag!.Tag, "Approve");
        Assert.That(formerAssignee.StatusCode, Is.EqualTo(HttpStatusCode.Forbidden));
        Assert.That((await ReadProblemAsync(formerAssignee)).Code, Is.EqualTo("step.not_assignee"));

        var (approved, _) = await DecideCurrentStepAsync(Finance2, submitted.Id);
        Assert.That(approved.Status, Is.EqualTo(RequestStatus.Approved));
    }

    [Test]
    public async Task Claim_AssignedStep_Returns409()
    {
        var (submitted, etag) = await SubmittedRequestAsync(Employee1);
        using var director = ClientFor(Director);

        using var response = await SendCommandAsync(director, HttpMethod.Post, $"/api/v1/requests/{submitted.Id}/steps/{submitted.Steps[0].Id}/claim", etag);

        Assert.That(response.StatusCode, Is.EqualTo(HttpStatusCode.Conflict));
        Assert.That((await ReadProblemAsync(response)).Code, Is.EqualTo("step.already_assigned"));
    }

    [Test]
    public async Task Reassign_ToIneligibleUser_Returns422()
    {
        var (submitted, etag) = await SubmittedRequestAsync(Employee1);
        using var admin = ClientFor(Admin);

        using var response = await SendCommandAsync(admin, HttpMethod.Put,
            $"/api/v1/requests/{submitted.Id}/steps/{submitted.Steps[0].Id}/assignee", etag, new { userId = Finance1.Id });

        Assert.That(response.StatusCode, Is.EqualTo(HttpStatusCode.UnprocessableEntity));
        Assert.That((await ReadProblemAsync(response)).Code, Is.EqualTo("user.not_eligible"));
    }

    [Test]
    public async Task Reassign_ToUnknownUser_Returns404()
    {
        var (submitted, etag) = await SubmittedRequestAsync(Employee1);
        using var admin = ClientFor(Admin);

        using var response = await SendCommandAsync(admin, HttpMethod.Put,
            $"/api/v1/requests/{submitted.Id}/steps/{submitted.Steps[0].Id}/assignee", etag, new { userId = 987654 });

        Assert.That(response.StatusCode, Is.EqualTo(HttpStatusCode.NotFound));
        Assert.That((await ReadProblemAsync(response)).Code, Is.EqualTo("user.not_found"));
    }
}
