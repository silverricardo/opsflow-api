using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using OpsFlow.Domain.Requests;
using OpsFlow.Domain.Users;

namespace OpsFlow.Api.Persistence.Configurations;

internal sealed class RequestConfiguration : IEntityTypeConfiguration<Request>
{
    public void Configure(EntityTypeBuilder<Request> builder)
    {
        builder.ToTable("Requests", t =>
        {
            t.HasCheckConstraint("CK_Requests_Amount", $"[Amount] > 0 AND [Amount] <= {Request.MaxAmount:0}");
            t.HasCheckConstraint("CK_Requests_Status", SqlConstraints.InEnum<RequestStatus>("Status"));
            t.HasCheckConstraint("CK_Requests_Category", SqlConstraints.InEnum<RequestCategory>("Category"));
            t.HasCheckConstraint("CK_Requests_CurrentRound", "[CurrentRound] >= 0");
        });

        // REQ-000042: derived from the identity, deterministic and persisted so it can be indexed (BR-02).
        builder.Property(r => r.Reference)
            .HasMaxLength(20)
            .IsUnicode(false)
            .HasComputedColumnSql(
                "'REQ-' + CASE WHEN [Id] < 1000000 THEN RIGHT('00000' + CAST([Id] AS varchar(19)), 6) ELSE CAST([Id] AS varchar(19)) END",
                stored: true);

        builder.Property(r => r.Title).HasMaxLength(Request.TitleMaxLength);
        builder.Property(r => r.Description).HasMaxLength(Request.DescriptionMaxLength);
        builder.Property(r => r.RowVersion).IsRowVersion();

        builder.HasOne<User>().WithMany().HasForeignKey(r => r.RequesterId).OnDelete(DeleteBehavior.Restrict);
        builder.HasMany(r => r.Steps).WithOne().HasForeignKey(s => s.RequestId).OnDelete(DeleteBehavior.Restrict);
        builder.HasMany(r => r.AuditEntries).WithOne().HasForeignKey(a => a.RequestId).OnDelete(DeleteBehavior.Restrict);

        builder.HasIndex(r => r.Reference).IsUnique();

        // "My requests", filtered by status, newest first.
        builder.HasIndex(r => new { r.RequesterId, r.Status, r.CreatedAt })
            .IsDescending(false, false, true)
            .IncludeProperties(r => new { r.Reference, r.Title, r.Category, r.Amount, r.UpdatedAt })
            .HasDatabaseName("IX_Requests_RequesterId_Status_CreatedAt");

        // Finance / Director / Admin lists filtered by status.
        builder.HasIndex(r => new { r.Status, r.CreatedAt })
            .IsDescending(false, true)
            .IncludeProperties(r => new { r.Reference, r.Title, r.Category, r.Amount, r.RequesterId, r.UpdatedAt })
            .HasDatabaseName("IX_Requests_Status_CreatedAt");
    }
}
