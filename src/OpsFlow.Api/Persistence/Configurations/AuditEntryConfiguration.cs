using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using OpsFlow.Domain.Auditing;
using OpsFlow.Domain.Requests;
using OpsFlow.Domain.Users;

namespace OpsFlow.Api.Persistence.Configurations;

internal sealed class AuditEntryConfiguration : IEntityTypeConfiguration<AuditEntry>
{
    /// <summary>INSTEAD OF UPDATE, DELETE trigger that makes the table append-only (BR-52), created in a migration.</summary>
    public const string AppendOnlyTrigger = "TR_AuditEntries_AppendOnly";

    public void Configure(EntityTypeBuilder<AuditEntry> builder)
    {
        builder.ToTable("AuditEntries", t =>
        {
            // Declared so EF Core avoids OUTPUT without INTO, which SQL Server rejects on tables with triggers.
            t.HasTrigger(AppendOnlyTrigger);
            t.HasCheckConstraint("CK_AuditEntries_FromStatus", SqlConstraints.InEnum<RequestStatus>("FromStatus"));
            t.HasCheckConstraint("CK_AuditEntries_ToStatus", SqlConstraints.InEnum<RequestStatus>("ToStatus"));
        });

        builder.Property(a => a.Action).HasMaxLength(AuditEntry.ActionMaxLength).IsUnicode(false);
        builder.Property<string>(OpsFlowDbContext.CorrelationIdProperty).HasMaxLength(64).IsUnicode(false).IsRequired();

        builder.HasOne<User>().WithMany().HasForeignKey(a => a.ActorId).OnDelete(DeleteBehavior.Restrict);

        builder.HasIndex(a => new { a.RequestId, a.OccurredAt });
    }
}
