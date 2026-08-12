using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using VirtualCompany.Domain.Entities;
using VirtualCompany.Domain.Enums;

namespace VirtualCompany.Infrastructure.Persistence;

internal sealed class OperatingDispatchConfiguration : IEntityTypeConfiguration<OperatingDispatch>
{
    public void Configure(EntityTypeBuilder<OperatingDispatch> b)
    {
        b.ToTable("operating_dispatches");
        b.HasKey(x => x.Id);
        b.Property(x => x.Id).HasColumnName("id");
        b.Property(x => x.CompanyId).HasColumnName("company_id");
        b.Property(x => x.InitiativeId).HasColumnName("initiative_id");
        b.Property(x => x.TaskId).HasColumnName("task_id");
        b.Property(x => x.Kind).HasColumnName("kind").HasMaxLength(32)
            .HasConversion(x => x.ToStorageValue(), x => OperatingDispatchKindValues.Parse(x));
        b.Property(x => x.Status).HasColumnName("status").HasMaxLength(32)
            .HasConversion(x => x.ToStorageValue(), x => OperatingDispatchStatusValues.Parse(x));
        b.Property(x => x.AttemptCount).HasColumnName("attempt_count");
        b.Property(x => x.MaxAttempts).HasColumnName("max_attempts");
        b.Property(x => x.NextAttemptUtc).HasColumnName("next_attempt_at");
        b.Property(x => x.LeaseOwner).HasColumnName("lease_owner").HasMaxLength(128);
        b.Property(x => x.LeaseExpiresUtc).HasColumnName("lease_expires_at");
        b.Property(x => x.OrchestrationRunId).HasColumnName("orchestration_run_id");
        b.Property(x => x.CollaborationPlanId).HasColumnName("collaboration_plan_id");
        b.Property(x => x.CorrelationId).HasColumnName("correlation_id").HasMaxLength(128);
        b.Property(x => x.FailureCode).HasColumnName("failure_code").HasMaxLength(100);
        b.Property(x => x.FailureSummary).HasColumnName("failure_summary").HasMaxLength(2000);
        b.Property(x => x.CreatedUtc).HasColumnName("created_at");
        b.Property(x => x.UpdatedUtc).HasColumnName("updated_at");
        b.Property(x => x.CompletedUtc).HasColumnName("completed_at");
        b.Property(x => x.Version).HasColumnName("version").IsConcurrencyToken();
        b.HasIndex(x => new { x.CompanyId, x.InitiativeId }).IsUnique();
        b.HasIndex(x => new { x.CompanyId, x.Status, x.NextAttemptUtc });
        b.HasIndex(x => new { x.Status, x.LeaseExpiresUtc });
        b.HasOne(x => x.Company).WithMany().HasForeignKey(x => x.CompanyId).OnDelete(DeleteBehavior.Cascade);
        b.HasOne(x => x.Initiative).WithMany().HasForeignKey(x => x.InitiativeId).OnDelete(DeleteBehavior.NoAction);
        b.HasOne(x => x.Task).WithMany().HasForeignKey(x => x.TaskId).OnDelete(DeleteBehavior.NoAction);
    }
}
