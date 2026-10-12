using Microsoft.Data.SqlClient;
using Microsoft.EntityFrameworkCore;
using static OpsFlow.Api.Tests.Integration.TestUsers;

namespace OpsFlow.Api.Tests.Integration;

[TestFixture]
public class AuditTrailTests : IntegrationTestBase
{
    // BR-52 / AC-21: the database itself rejects changes to the audit trail.
    [TestCase("UPDATE [AuditEntries] SET [Action] = 'Tampered'")]
    [TestCase("DELETE FROM [AuditEntries]")]
    public async Task AuditEntries_CannotBeModifiedOrDeleted(string sql)
    {
        await CreateRequestAsync(Employee1);

        var exception = Assert.ThrowsAsync<SqlException>(() => QueryDbAsync(db => db.Database.ExecuteSqlRawAsync(sql)));

        Assert.Multiple(async () =>
        {
            Assert.That(exception!.Number, Is.EqualTo(50001));
            Assert.That(await QueryDbAsync(db => db.AuditEntries.CountAsync(a => a.Action == "RequestCreated")), Is.EqualTo(1));
        });
    }
}
