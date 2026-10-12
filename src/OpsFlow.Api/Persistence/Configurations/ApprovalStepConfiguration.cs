using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using OpsFlow.Domain.Requests;
using OpsFlow.Domain.Users;

namespace OpsFlow.Api.Persistence.Configurations;

internal sealed class ApprovalStepConfiguration : IEntityTypeConfiguration<ApprovalStep>
{
    private const string CurrentFilter = "[IsCurrent] = 1";

    public void Configure(EntityTypeBuilder<ApprovalStep> builder)
    {
        builder.ToTable("ApprovalSteps", t =>
        {
            t.HasCheckConstraint("CK_ApprovalSteps_Status", SqlConstraints.InEnum<StepStatus>("Status"));
            t.HasCheckConstraint("CK_ApprovalSteps_Kind", SqlConstraints.InEnum<StepKind>("Kind"));
            t.HasCheckConstraint("CK_ApprovalSteps_Round", "[Round] >= 1");
            t.HasCheckConstraint("CK_ApprovalSteps_CurrentIsPending", "[IsCurrent] = 0 OR [Status] = 'Pending'");
        });

        builder.Property(s => s.Comment).HasMaxLength(ApprovalStep.CommentMaxLength);

        builder.HasOne<User>().WithMany().HasForeignKey(s => s.AssigneeId).OnDelete(DeleteBehavior.Restrict);
        builder.HasOne<User>().WithMany().HasForeignKey(s => s.DecidedById).OnDelete(DeleteBehavior.Restrict);

        // Visibility (BR-40): "requests where I am or was the assignee / the decider", any status.
        // Named indexes: an unnamed HasIndex on the same column would return (and reconfigure) one index.
        builder.HasIndex(s => s.AssigneeId, "IX_ApprovalSteps_AssigneeId").IncludeProperties(s => s.RequestId);
        builder.HasIndex(s => s.DecidedById, "IX_ApprovalSteps_DecidedById").IncludeProperties(s => s.RequestId);

        builder.HasIndex(s => new { s.RequestId, s.Round, s.StepOrder }).IsUnique();

        // Inbox: the current (actionable) step, assigned to me. Only one row per request in approval.
        builder.HasIndex(s => s.AssigneeId, "IX_ApprovalSteps_Current_Assignee")
            .HasFilter(CurrentFilter)
            .IncludeProperties(s => new { s.RequestId, s.Round, s.Kind });

        // Inbox: the current step, unassigned, in my role's pool.
        builder.HasIndex(s => s.Kind, "IX_ApprovalSteps_Current_Pool")
            .HasFilter($"{CurrentFilter} AND [AssigneeId] IS NULL")
            .IncludeProperties(s => new { s.RequestId, s.Round });
    }
}
