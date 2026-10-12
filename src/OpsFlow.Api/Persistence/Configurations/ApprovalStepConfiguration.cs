using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using OpsFlow.Domain.Requests;
using OpsFlow.Domain.Users;

namespace OpsFlow.Api.Persistence.Configurations;

internal sealed class ApprovalStepConfiguration : IEntityTypeConfiguration<ApprovalStep>
{
    private const string PendingFilter = "[Status] = 'Pending'";

    public void Configure(EntityTypeBuilder<ApprovalStep> builder)
    {
        builder.ToTable("ApprovalSteps", t =>
        {
            t.HasCheckConstraint("CK_ApprovalSteps_Status", SqlConstraints.InEnum<StepStatus>("Status"));
            t.HasCheckConstraint("CK_ApprovalSteps_Kind", SqlConstraints.InEnum<StepKind>("Kind"));
            t.HasCheckConstraint("CK_ApprovalSteps_Round", "[Round] >= 1");
        });

        builder.Property(s => s.Comment).HasMaxLength(ApprovalStep.CommentMaxLength);

        builder.HasOne<User>().WithMany().HasForeignKey(s => s.AssigneeId).OnDelete(DeleteBehavior.Restrict);
        builder.HasOne<User>().WithMany().HasForeignKey(s => s.DecidedById).OnDelete(DeleteBehavior.Restrict);

        // Visibility (BR-40): "requests where I am or was the assignee / the decider", any status.
        // Named indexes: an unnamed HasIndex on the same column would return (and reconfigure) one index.
        builder.HasIndex(s => s.AssigneeId, "IX_ApprovalSteps_AssigneeId").IncludeProperties(s => s.RequestId);
        builder.HasIndex(s => s.DecidedById, "IX_ApprovalSteps_DecidedById").IncludeProperties(s => s.RequestId);

        builder.HasIndex(s => new { s.RequestId, s.Round, s.StepOrder }).IsUnique();

        // Inbox: pending steps assigned to me.
        builder.HasIndex(s => s.AssigneeId, "IX_ApprovalSteps_AssigneeId_Pending")
            .HasFilter(PendingFilter)
            .IncludeProperties(s => new { s.RequestId, s.Round, s.StepOrder, s.Kind });

        // Inbox: unassigned pending steps in my role's pool.
        builder.HasIndex(s => s.Kind, "IX_ApprovalSteps_Kind_PendingUnassigned")
            .HasFilter($"{PendingFilter} AND [AssigneeId] IS NULL")
            .IncludeProperties(s => new { s.RequestId, s.Round, s.StepOrder });
    }
}
