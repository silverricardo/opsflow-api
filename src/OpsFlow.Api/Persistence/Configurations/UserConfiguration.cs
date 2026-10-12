using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using OpsFlow.Domain.Users;

namespace OpsFlow.Api.Persistence.Configurations;

internal sealed class UserConfiguration : IEntityTypeConfiguration<User>
{
    public void Configure(EntityTypeBuilder<User> builder)
    {
        builder.ToTable("Users", t => t.HasCheckConstraint("CK_Users_Role", SqlConstraints.InEnum<UserRole>("Role")));

        builder.Property(u => u.Email).HasMaxLength(User.EmailMaxLength);
        builder.Property(u => u.DisplayName).HasMaxLength(User.DisplayNameMaxLength);
        builder.Property(u => u.PasswordHash).HasMaxLength(512).IsUnicode(false);

        builder.HasIndex(u => u.Email).IsUnique();

        builder.HasOne<User>().WithMany().HasForeignKey(u => u.ManagerId).OnDelete(DeleteBehavior.Restrict);
    }
}
