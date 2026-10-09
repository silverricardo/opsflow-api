using Microsoft.EntityFrameworkCore;
using OpsFlow.Api.Infrastructure.Correlation;
using OpsFlow.Domain.Auditing;
using OpsFlow.Domain.Requests;
using OpsFlow.Domain.Users;

namespace OpsFlow.Api.Persistence;

/// <summary>
/// Unit of work and query surface. Used directly by endpoints — there is no repository layer (ADR-002).
/// </summary>
public sealed class OpsFlowDbContext(DbContextOptions<OpsFlowDbContext> options, CorrelationContext correlation)
    : DbContext(options)
{
    public const string CorrelationIdProperty = "CorrelationId";

    public DbSet<User> Users => Set<User>();

    public DbSet<Request> Requests => Set<Request>();

    public DbSet<ApprovalStep> ApprovalSteps => Set<ApprovalStep>();

    public DbSet<ApprovalPolicyRule> ApprovalPolicyRules => Set<ApprovalPolicyRule>();

    public DbSet<AuditEntry> AuditEntries => Set<AuditEntry>();

    public override int SaveChanges(bool acceptAllChangesOnSuccess)
    {
        StampAuditEntries();
        return base.SaveChanges(acceptAllChangesOnSuccess);
    }

    public override Task<int> SaveChangesAsync(bool acceptAllChangesOnSuccess, CancellationToken cancellationToken = default)
    {
        StampAuditEntries();
        return base.SaveChangesAsync(acceptAllChangesOnSuccess, cancellationToken);
    }

    protected override void OnModelCreating(ModelBuilder modelBuilder) =>
        modelBuilder.ApplyConfigurationsFromAssembly(typeof(OpsFlowDbContext).Assembly);

    protected override void ConfigureConventions(ModelConfigurationBuilder configurationBuilder)
    {
        configurationBuilder.Properties<DateTimeOffset>().HavePrecision(3);
        configurationBuilder.Properties<decimal>().HavePrecision(18, 2);

        // Enums are stored by name: readable in SQL and in support queries (see design.md §2).
        configurationBuilder.Properties<UserRole>().HaveConversion<string>().HaveMaxLength(32).AreUnicode(false);
        configurationBuilder.Properties<RequestStatus>().HaveConversion<string>().HaveMaxLength(32).AreUnicode(false);
        configurationBuilder.Properties<RequestCategory>().HaveConversion<string>().HaveMaxLength(32).AreUnicode(false);
        configurationBuilder.Properties<StepKind>().HaveConversion<string>().HaveMaxLength(32).AreUnicode(false);
        configurationBuilder.Properties<StepStatus>().HaveConversion<string>().HaveMaxLength(32).AreUnicode(false);
    }

    /// <summary>The correlation ID is infrastructure, not domain: it is stamped here as a shadow property.</summary>
    private void StampAuditEntries()
    {
        foreach (var entry in ChangeTracker.Entries<AuditEntry>().Where(e => e.State == EntityState.Added))
        {
            entry.Property(CorrelationIdProperty).CurrentValue = correlation.CorrelationId;
        }
    }
}
