using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using VirtualCompany.Domain.Entities;
using VirtualCompany.Domain.Enums;

namespace VirtualCompany.Infrastructure.Persistence;

internal sealed class OperatingCycleRequestConfiguration : IEntityTypeConfiguration<OperatingCycleRequest>
{
    public void Configure(EntityTypeBuilder<OperatingCycleRequest> b)
    {
        b.ToTable("operating_cycle_requests"); b.HasKey(x => x.Id);
        b.Property(x => x.Id).HasColumnName("id"); b.Property(x => x.CompanyId).HasColumnName("company_id");
        b.Property(x => x.OperatingEventId).HasColumnName("operating_event_id"); b.Property(x => x.OperatingCycleId).HasColumnName("operating_cycle_id");
        b.Property(x => x.TriggerType).HasColumnName("trigger_type").HasMaxLength(64); b.Property(x => x.TriggerReference).HasColumnName("trigger_reference").HasMaxLength(256);
        b.Property(x => x.DeduplicationKey).HasColumnName("deduplication_key").HasMaxLength(200); b.Property(x => x.CorrelationId).HasColumnName("correlation_id").HasMaxLength(128);
        b.Property(x => x.Status).HasColumnName("status").HasMaxLength(32).HasConversion(x => x.ToStorageValue(), x => OperatingCycleRequestStatusValues.Parse(x));
        b.Property(x => x.NotBeforeUtc).HasColumnName("not_before_at"); b.Property(x => x.AttemptCount).HasColumnName("attempt_count"); b.Property(x => x.MaxAttempts).HasColumnName("max_attempts");
        b.Property(x => x.LeaseOwner).HasColumnName("lease_owner").HasMaxLength(128); b.Property(x => x.LeaseExpiresUtc).HasColumnName("lease_expires_at");
        b.Property(x => x.FailureCode).HasColumnName("failure_code").HasMaxLength(100); b.Property(x => x.FailureSummary).HasColumnName("failure_summary").HasMaxLength(2000);
        b.Property(x => x.CreatedUtc).HasColumnName("created_at"); b.Property(x => x.UpdatedUtc).HasColumnName("updated_at"); b.Property(x => x.CompletedUtc).HasColumnName("completed_at");
        b.Property(x => x.Version).HasColumnName("version").IsConcurrencyToken();
        b.HasIndex(x => new { x.CompanyId, x.DeduplicationKey }).IsUnique(); b.HasIndex(x => new { x.Status, x.NotBeforeUtc, x.LeaseExpiresUtc });
        b.HasOne(x => x.Company).WithMany().HasForeignKey(x => x.CompanyId).OnDelete(DeleteBehavior.Cascade);
        b.HasOne(x => x.OperatingEvent).WithMany().HasForeignKey(x => x.OperatingEventId).OnDelete(DeleteBehavior.NoAction);
        b.HasOne(x => x.OperatingCycle).WithMany().HasForeignKey(x => x.OperatingCycleId).OnDelete(DeleteBehavior.NoAction);
    }
}
