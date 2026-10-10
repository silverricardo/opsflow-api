using System.Net;
using Microsoft.EntityFrameworkCore;
using OpsFlow.Api.Features.Requests;
using OpsFlow.Domain.Auditing;
using OpsFlow.Domain.Requests;
using static OpsFlow.Api.Tests.Integration.TestUsers;

namespace OpsFlow.Api.Tests.Integration;

[TestFixture]
public class SubmitRequestTests : IntegrationTestBase
{
    [Test]
    public async Task Submit_SmallAmount_CreatesManagerStepAssignedToRequestersManager()
    {
        var (created, etag) = await CreateRequestAsync(Employee1, amount: 1_200m);
        using var client = ClientFor(Employee1);

        using var response = await SubmitAsync(client, created.Id, etag);

        Assert.That(response.StatusCode, Is.EqualTo(HttpStatusCode.OK), await response.Content.ReadAsStringAsync());
        var body = await ReadAsync<RequestDetailsResponse>(response);
        Assert.Multiple(() =>
        {
            Assert.That(body.Status, Is.EqualTo(RequestStatus.InApproval));
            Assert.That(body.CurrentRound, Is.EqualTo(1));
            Assert.That(body.SubmittedAt, Is.Not.Null);
            Assert.That(response.Headers.ETag?.Tag, Is.Not.EqualTo(etag), "the version changes");
            Assert.That(body.Steps, Has.Count.EqualTo(1));
            Assert.That(body.Steps[0].Kind, Is.EqualTo(StepKind.Manager));
            Assert.That(body.Steps[0].Status, Is.EqualTo(StepStatus.Pending));
            Assert.That(body.Steps[0].Assignee, Is.EqualTo(new UserSummary(Manager1.Id, Manager1.DisplayName)));
        });
    }

    [Test]
    public async Task Submit_LargeAmount_CreatesManagerFinanceAndDirectorSteps()
    {
        var (created, etag) = await CreateRequestAsync(Employee3, amount: 75_000m);
        using var client = ClientFor(Employee3);

        using var response = await SubmitAsync(client, created.Id, etag);

        var body = await ReadAsync<RequestDetailsResponse>(response);
        Assert.Multiple(() =>
        {
            Assert.That(body.Steps.Select(s => s.Kind), Is.EqualTo(new[] { StepKind.Manager, StepKind.Finance, StepKind.Director }));
            Assert.That(body.Steps.Select(s => s.Assignee?.Id), Is.EqualTo(new int?[] { Manager2.Id, null, null }));
        });
    }

    [Test]
    public async Task Submit_WritesAuditEntriesInOrder()
    {
        var (created, etag) = await CreateRequestAsync(Employee1);
        using var client = ClientFor(Employee1);

        using var response = await SubmitAsync(client, created.Id, etag);

        var actions = await QueryDbAsync(db => db.AuditEntries
            .Where(a => a.RequestId == created.Id)
            .OrderBy(a => a.Id)
            .Select(a => a.Action)
            .ToListAsync());
        Assert.That(actions, Is.EqualTo(new[] { AuditActions.RequestCreated, AuditActions.RequestSubmitted }));
    }

    [Test]
    public async Task Submit_WithoutIfMatch_Returns428()
    {
        var (created, _) = await CreateRequestAsync(Employee1);
        using var client = ClientFor(Employee1);

        using var response = await SubmitAsync(client, created.Id, etag: null);

        Assert.That(response.StatusCode, Is.EqualTo(HttpStatusCode.PreconditionRequired));
        Assert.That((await ReadProblemAsync(response)).Code, Is.EqualTo("concurrency.if_match_required"));
    }

    [Test]
    public async Task Submit_WithStaleETag_Returns412AndChangesNothing()
    {
        var (created, _) = await CreateRequestAsync(Employee1);
        using var client = ClientFor(Employee1);

        using var response = await SubmitAsync(client, created.Id, "\"AAAAAAAAAAA=\"");

        Assert.That(response.StatusCode, Is.EqualTo(HttpStatusCode.PreconditionFailed));
        Assert.Multiple(async () =>
        {
            Assert.That((await ReadProblemAsync(response)).Code, Is.EqualTo("concurrency.etag_mismatch"));
            Assert.That(await QueryDbAsync(db => db.Requests.Where(r => r.Id == created.Id).Select(r => r.Status).SingleAsync()),
                Is.EqualTo(RequestStatus.Draft));
        });
    }

    [Test]
    public async Task Submit_WhenRequesterHasNoManager_Returns422()
    {
        var (created, etag) = await CreateRequestAsync(Director);
        using var client = ClientFor(Director);

        using var response = await SubmitAsync(client, created.Id, etag);

        Assert.That(response.StatusCode, Is.EqualTo(HttpStatusCode.UnprocessableEntity));
        Assert.That((await ReadProblemAsync(response)).Code, Is.EqualTo("request.requester_has_no_manager"));
    }

    [Test]
    public async Task Submit_ByRequestersManager_Returns403NotOwner()
    {
        var (created, etag) = await CreateRequestAsync(Employee1);
        using var client = ClientFor(Manager1);

        using var response = await SubmitAsync(client, created.Id, etag);

        Assert.That(response.StatusCode, Is.EqualTo(HttpStatusCode.Forbidden));
        Assert.That((await ReadProblemAsync(response)).Code, Is.EqualTo("request.not_owner"));
    }

    [Test]
    public async Task Submit_ByUserWhoCannotSeeTheRequest_Returns404()
    {
        var (created, etag) = await CreateRequestAsync(Employee1);
        using var client = ClientFor(Employee2);

        using var response = await SubmitAsync(client, created.Id, etag);

        Assert.That(response.StatusCode, Is.EqualTo(HttpStatusCode.NotFound));
    }

    [Test]
    public async Task Submit_AlreadySubmittedRequestWithFreshETag_Returns409()
    {
        var (created, etag) = await CreateRequestAsync(Employee1);
        using var client = ClientFor(Employee1);
        using var first = await SubmitAsync(client, created.Id, etag);

        using var second = await SubmitAsync(client, created.Id, first.Headers.ETag!.Tag);

        Assert.That(second.StatusCode, Is.EqualTo(HttpStatusCode.Conflict));
        Assert.That((await ReadProblemAsync(second)).Code, Is.EqualTo("request.invalid_transition"));
    }

    [Test]
    [Repeat(20)] // races are timing-dependent: repeat so an intermittent failure cannot hide
    public async Task Submit_TwiceConcurrentlyWithSameETag_ExactlyOneSucceeds()
    {
        var (created, etag) = await CreateRequestAsync(Employee1);
        using var clientA = ClientFor(Employee1);
        using var clientB = ClientFor(Employee1);

        var responses = await Task.WhenAll(SubmitAsync(clientA, created.Id, etag), SubmitAsync(clientB, created.Id, etag));

        try
        {
            Assert.That(responses.Select(r => r.StatusCode),
                Is.EquivalentTo(new[] { HttpStatusCode.OK, HttpStatusCode.PreconditionFailed }));
            var stepCount = await QueryDbAsync(db => db.ApprovalSteps.CountAsync(s => s.RequestId == created.Id));
            Assert.That(stepCount, Is.EqualTo(1), "the losing submission left no partial changes");
        }
        finally
        {
            foreach (var response in responses)
            {
                response.Dispose();
            }
        }
    }

    // BR-12: an inactive manager counts as no manager.
    [Test]
    public async Task Submit_WhenManagerIsInactive_Returns422()
    {
        var (created, etag) = await CreateRequestAsync(OrphanedEmployee);
        using var client = ClientFor(OrphanedEmployee);

        using var response = await SubmitAsync(client, created.Id, etag);

        Assert.That(response.StatusCode, Is.EqualTo(HttpStatusCode.UnprocessableEntity));
        Assert.That((await ReadProblemAsync(response)).Code, Is.EqualTo("request.requester_has_no_manager"));
    }

    // BR-13a: Manager1's manager is the Director; without a second active Director the chain could never finish.
    [Test]
    public async Task Submit_WhenNoOneCouldDecideALaterStep_Returns422InsteadOfGettingStuck()
    {
        var (created, etag) = await CreateRequestAsync(Manager1, 60_000m);
        await QueryDbAsync(db => db.Database.ExecuteSqlAsync($"UPDATE [Users] SET [IsActive] = 0 WHERE [Id] = {Director2.Id}"));
        try
        {
            using var client = ClientFor(Manager1);

            using var response = await SubmitAsync(client, created.Id, etag);

            Assert.That(response.StatusCode, Is.EqualTo(HttpStatusCode.UnprocessableEntity));
            Assert.That((await ReadProblemAsync(response)).Code, Is.EqualTo("request.no_eligible_approver"));
        }
        finally
        {
            await QueryDbAsync(db => db.Database.ExecuteSqlAsync($"UPDATE [Users] SET [IsActive] = 1 WHERE [Id] = {Director2.Id}"));
        }
    }
}
