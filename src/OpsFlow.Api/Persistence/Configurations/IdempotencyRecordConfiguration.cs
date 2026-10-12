using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using OpsFlow.Domain.Users;

namespace OpsFlow.Api.Persistence.Configurations;

internal sealed class IdempotencyRecordConfiguration : IEntityTypeConfiguration<IdempotencyRecord>
{
    public void Configure(EntityTypeBuilder<IdempotencyRecord> builder)
    {
        builder.ToTable("IdempotencyRecords");

        // The primary key settles concurrent duplicates: only one reservation per user and key can exist.
        builder.HasKey(r => new { r.UserId, r.Key });

        builder.Property(r => r.Key).HasMaxLength(IdempotencyRecord.KeyMaxLength).IsUnicode(false);
        builder.Property(r => r.Endpoint).HasMaxLength(300).IsUnicode(false);
        builder.Property(r => r.RequestHash).HasMaxLength(32).IsFixedLength();
        builder.Property(r => r.ResponseContentType).HasMaxLength(100).IsUnicode(false);
        builder.Property(r => r.ResponseETag).HasMaxLength(100).IsUnicode(false);
        builder.Property(r => r.ResponseLocation).HasMaxLength(300).IsUnicode(false);
        builder.Ignore(r => r.IsCompleted);

        builder.HasOne<User>().WithMany().HasForeignKey(r => r.UserId).OnDelete(DeleteBehavior.Restrict);

        builder.HasIndex(r => r.ExpiresAt);
    }
}
