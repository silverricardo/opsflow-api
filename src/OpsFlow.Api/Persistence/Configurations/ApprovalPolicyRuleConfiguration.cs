using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using OpsFlow.Domain.Requests;

namespace OpsFlow.Api.Persistence.Configurations;

internal sealed class ApprovalPolicyRuleConfiguration : IEntityTypeConfiguration<ApprovalPolicyRule>
{
    public void Configure(EntityTypeBuilder<ApprovalPolicyRule> builder)
    {
        builder.ToTable("ApprovalPolicyRules", t =>
        {
            t.HasCheckConstraint("CK_ApprovalPolicyRules_Kind", SqlConstraints.InEnum<StepKind>("Kind"));
            t.HasCheckConstraint("CK_ApprovalPolicyRules_AmountGreaterThan", "[AmountGreaterThan] >= 0");
        });

        builder.Property(r => r.Id).ValueGeneratedNever();
        builder.HasIndex(r => r.StepOrder).IsUnique();

        // Reference data the system cannot work without (BR-11), so it ships with the migrations.
        builder.HasData(
            new ApprovalPolicyRule(1, 1, StepKind.Manager, 0m),
            new ApprovalPolicyRule(2, 2, StepKind.Finance, 5_000m),
            new ApprovalPolicyRule(3, 3, StepKind.Director, 50_000m));
    }
}
