using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using OpsFlow.Api.Persistence;
using OpsFlow.Domain.Requests;
using static OpsFlow.Api.Tests.Integration.TestUsers;

namespace OpsFlow.Api.Tests.Integration;

/// <summary>
/// Deterministically exercises the race the ETag pre-check cannot catch: both writers load the same
/// version, the first saves, and the second must fail on the rowversion predicate (ADR-004).
/// </summary>
[TestFixture]
public class OptimisticConcurrencyTests : IntegrationTestBase
{
    [Test]
    public async Task SaveChanges_WhenAnotherWriterSavedFirst_ThrowsConcurrencyExceptionAndWritesNothing()
    {
        var (created, _) = await CreateRequestAsync(Employee1);
        var services = IntegrationTestEnvironment.Factory.Services;
        await using var scopeA = services.CreateAsyncScope();
        await using var scopeB = services.CreateAsyncScope();
        var dbA = scopeA.ServiceProvider.GetRequiredService<OpsFlowDbContext>();
        var dbB = scopeB.ServiceProvider.GetRequiredService<OpsFlowDbContext>();
        var actor = new Actor(Employee1.Id, Employee1.Role);

        var copyA = await dbA.Requests.SingleAsync(r => r.Id == created.Id);
        var copyB = await dbB.Requests.SingleAsync(r => r.Id == created.Id);

        Assert.That(copyA.Update(actor, "Writer A", null, RequestCategory.Expense, 10m, DateTimeOffset.UtcNow).IsSuccess, Is.True);
        await dbA.SaveChangesAsync();

        Assert.That(copyB.Update(actor, "Writer B", null, RequestCategory.Expense, 20m, DateTimeOffset.UtcNow).IsSuccess, Is.True);
        Assert.ThrowsAsync<DbUpdateConcurrencyException>(() => dbB.SaveChangesAsync());

        Assert.Multiple(async () =>
        {
            Assert.That(await QueryDbAsync(db => db.Requests.Where(r => r.Id == created.Id).Select(r => r.Title).SingleAsync()),
                Is.EqualTo("Writer A"));
            Assert.That(await QueryDbAsync(db => db.AuditEntries.CountAsync(a => a.RequestId == created.Id)),
                Is.EqualTo(2), "created + writer A; writer B's audit entry was rolled back with its update");
        });
    }
}
