using System.Net;
using OpsFlow.Api.Features.Approvals;
using OpsFlow.Api.Features.Common;
using OpsFlow.Domain.Requests;
using static OpsFlow.Api.Tests.Integration.TestUsers;

namespace OpsFlow.Api.Tests.Integration;

[TestFixture]
public class InboxTests : IntegrationTestBase
{
    [Test]
    public async Task Inbox_ShowsAssignedCurrentStepsOnly_UntilEarlierStepsAreDecided()
    {
        var (submitted, _) = await SubmittedRequestAsync(Employee1, 7_000m);

        var managerInbox = await InboxAsync(Manager1);
        var financeBefore = await InboxAsync(Finance1);

        Assert.Multiple(() =>
        {
            Assert.That(managerInbox.Items.Single().RequestId, Is.EqualTo(submitted.Id));
            Assert.That(managerInbox.Items.Single().AssignedToMe, Is.True);
            Assert.That(managerInbox.Items.Single().StepKind, Is.EqualTo(StepKind.Manager));
            Assert.That(financeBefore.Items, Is.Empty, "the Finance step is not current yet");
        });

        await DecideCurrentStepAsync(Manager1, submitted.Id);
        var financeAfter = await InboxAsync(Finance1);
        var managerAfter = await InboxAsync(Manager1);

        Assert.Multiple(() =>
        {
            Assert.That(financeAfter.Items.Single().StepKind, Is.EqualTo(StepKind.Finance));
            Assert.That(financeAfter.Items.Single().AssignedToMe, Is.False, "pool step");
            Assert.That(managerAfter.Items, Is.Empty);
        });
    }

    [Test]
    public async Task Inbox_ExcludesOwnRequestsAndRoundsAlreadyDecided()
    {
        // Finance1's own request reaches the Finance step: Finance1 must not see it, Finance2 must.
        var (own, _) = await SubmittedRequestAsync(Finance1, 7_000m);
        await DecideCurrentStepAsync(Director, own.Id);

        // The Director decided the Manager step of Manager1's request, so the Director step is not theirs.
        var (managers, _) = await SubmittedRequestAsync(Manager1, 60_000m);
        await DecideCurrentStepAsync(Director, managers.Id);
        await DecideCurrentStepAsync(Finance2, managers.Id);

        var finance1 = await InboxAsync(Finance1);
        var finance2 = await InboxAsync(Finance2);
        var director = await InboxAsync(Director);

        Assert.Multiple(() =>
        {
            Assert.That(finance1.Items.Select(i => i.RequestId), Does.Not.Contain(own.Id));
            Assert.That(finance2.Items.Select(i => i.RequestId), Does.Contain(own.Id));
            Assert.That(director.Items.Select(i => i.RequestId), Does.Not.Contain(managers.Id));
        });
    }

    [TestCase("Employee1")]
    [TestCase("Admin")]
    public async Task Inbox_ForNonApprover_Returns403(string user)
    {
        using var client = ClientFor((TestUser)typeof(TestUsers).GetField(user)!.GetValue(null)!);

        using var response = await client.GetAsync(new Uri("/api/v1/approvals/inbox", UriKind.Relative));

        Assert.That(response.StatusCode, Is.EqualTo(HttpStatusCode.Forbidden));
    }

    private static async Task<PagedResponse<InboxItemResponse>> InboxAsync(TestUser user)
    {
        using var client = ClientFor(user);
        using var response = await client.GetAsync(new Uri("/api/v1/approvals/inbox", UriKind.Relative));
        Assert.That(response.StatusCode, Is.EqualTo(HttpStatusCode.OK), await response.Content.ReadAsStringAsync());
        return await ReadAsync<PagedResponse<InboxItemResponse>>(response);
    }
}
