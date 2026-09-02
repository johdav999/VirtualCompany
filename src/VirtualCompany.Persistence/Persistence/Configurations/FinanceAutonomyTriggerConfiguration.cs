using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using VirtualCompany.Domain.Entities;
using VirtualCompany.Domain.Enums;

namespace VirtualCompany.Infrastructure.Persistence;

internal sealed class FinanceAutonomyTriggerCursorConfiguration : IEntityTypeConfiguration<FinanceAutonomyTriggerCursor>
{
    public void Configure(EntityTypeBuilder<FinanceAutonomyTriggerCursor> b)
    {
        b.ToTable("finance_autonomy_trigger_cursors");
        b.HasKey(x => x.Id);
        b.HasAlternateKey(x => new { x.CompanyId, x.Id });
        b.Property(x => x.Id).HasColumnName("id");
        b.Property(x => x.CompanyId).HasColumnName("company_id").IsRequired();
        b.Property(x => x.GrantId).HasColumnName("grant_id").IsRequired();
        b.Property(x => x.GrantVersionId).HasColumnName("grant_version_id").IsRequired();
        b.Property(x => x.AgentId).HasColumnName("agent_id").IsRequired();
        b.Property(x => x.CapabilityId).HasColumnName("capability_id").HasMaxLength(160).IsRequired();
        b.Property(x => x.TriggerKind).HasColumnName("trigger_kind").HasMaxLength(32).IsRequired();
        b.Property(x => x.TriggerKey).HasColumnName("trigger_key").HasMaxLength(200).IsRequired();
        b.Property(x => x.Status).HasColumnName("status")
            .HasConversion(x => x.ToStorageValue(), x => FinanceAutonomyTriggerEnumValues.ParseCursorStatus(x))
            .HasMaxLength(32).IsRequired();
        b.Property(x => x.CursorUtc).HasColumnName("cursor_utc");
        b.Property(x => x.LastEventVersion).HasColumnName("last_event_version").HasMaxLength(100);
        b.Property(x => x.CurrentWindowStartUtc).HasColumnName("current_window_start_utc");
        b.Property(x => x.CurrentWindowEndUtc).HasColumnName("current_window_end_utc");
        b.Property(x => x.QuotaWindowStartUtc).HasColumnName("quota_window_start_utc");
        b.Property(x => x.QuotaWindowEndUtc).HasColumnName("quota_window_end_utc");
        b.Property(x => x.RunsInWindow).HasColumnName("runs_in_window").IsRequired();
        b.Property(x => x.LastRunId).HasColumnName("last_run_id");
        b.Property(x => x.LastRunUtc).HasColumnName("last_run_utc");
        b.Property(x => x.NextEligibleUtc).HasColumnName("next_eligible_utc");
        b.Property(x => x.AttemptCount).HasColumnName("attempt_count").IsRequired();
        b.Property(x => x.LeaseOwner).HasColumnName("lease_owner").HasMaxLength(160);
        b.Property(x => x.LeaseToken).HasColumnName("lease_token").HasMaxLength(160);
        b.Property(x => x.LeaseExpiresUtc).HasColumnName("lease_expires_utc");
        b.Property(x => x.FailureCode).HasColumnName("failure_code").HasMaxLength(100);
        b.Property(x => x.FailureSummary).HasColumnName("failure_summary").HasMaxLength(1000);
        b.Property(x => x.CreatedUtc).HasColumnName("created_utc").IsRequired();
        b.Property(x => x.UpdatedUtc).HasColumnName("updated_utc").IsRequired();
        b.Property(x => x.Version).HasColumnName("version").IsConcurrencyToken();
        b.Property(x => x.RowVersion).HasColumnName("row_version").HasMaxLength(16).IsFixedLength()
            .IsConcurrencyToken().ValueGeneratedNever();
        b.HasIndex(x => new { x.CompanyId, x.GrantVersionId, x.TriggerKind, x.TriggerKey }).IsUnique();
        b.HasIndex(x => new { x.Status, x.NextEligibleUtc, x.LeaseExpiresUtc, x.UpdatedUtc });
        b.HasIndex(x => new { x.CompanyId, x.Status, x.UpdatedUtc });
        b.HasOne(x => x.Grant).WithMany().HasForeignKey(x => new { x.CompanyId, x.GrantId })
            .HasPrincipalKey(x => new { x.CompanyId, x.Id }).OnDelete(DeleteBehavior.Cascade);
        b.HasOne(x => x.GrantVersion).WithMany().HasForeignKey(x => new { x.CompanyId, x.GrantVersionId })
            .HasPrincipalKey(x => new { x.CompanyId, x.Id }).OnDelete(DeleteBehavior.Restrict);
        b.HasOne(x => x.LastRun).WithMany().HasForeignKey(x => x.LastRunId).OnDelete(DeleteBehavior.NoAction);
    }
}

internal sealed class FinanceAutonomyTriggerEventConfiguration : IEntityTypeConfiguration<FinanceAutonomyTriggerEvent>
{
    public void Configure(EntityTypeBuilder<FinanceAutonomyTriggerEvent> b)
    {
        b.ToTable("finance_autonomy_trigger_events");
        b.HasKey(x => x.Id);
        b.Property(x => x.Id).HasColumnName("id");
        b.Property(x => x.CompanyId).HasColumnName("company_id").IsRequired();
        b.Property(x => x.CursorId).HasColumnName("cursor_id").IsRequired();
        b.Property(x => x.EventType).HasColumnName("event_type").HasMaxLength(100).IsRequired();
        b.Property(x => x.SourceEventId).HasColumnName("source_event_id").HasMaxLength(240).IsRequired();
        b.Property(x => x.SourceEventVersion).HasColumnName("source_event_version").HasMaxLength(100).IsRequired();
        b.Property(x => x.SourceEntityType).HasColumnName("source_entity_type").HasMaxLength(100).IsRequired();
        b.Property(x => x.SourceEntityId).HasColumnName("source_entity_id").HasMaxLength(240).IsRequired();
        b.Property(x => x.OccurredUtc).HasColumnName("occurred_utc").IsRequired();
        b.Property(x => x.EvidenceObservedUtc).HasColumnName("evidence_observed_utc").IsRequired();
        b.Property(x => x.CoalescingKey).HasColumnName("coalescing_key").HasMaxLength(200).IsRequired();
        b.Property(x => x.ContentHash).HasColumnName("content_hash").HasMaxLength(64).IsRequired();
        b.Property(x => x.SafeLabel).HasColumnName("safe_label").HasMaxLength(300);
        b.Property(x => x.CorrelationId).HasColumnName("correlation_id").HasMaxLength(128).IsRequired();
        b.Property(x => x.Status).HasColumnName("status")
            .HasConversion(x => x.ToStorageValue(), x => FinanceAutonomyTriggerEnumValues.ParseEventStatus(x))
            .HasMaxLength(32).IsRequired();
        b.Property(x => x.RunId).HasColumnName("run_id");
        b.Property(x => x.FailureCode).HasColumnName("failure_code").HasMaxLength(100);
        b.Property(x => x.FailureSummary).HasColumnName("failure_summary").HasMaxLength(1000);
        b.Property(x => x.CreatedUtc).HasColumnName("created_utc").IsRequired();
        b.Property(x => x.ProcessedUtc).HasColumnName("processed_utc");
        b.HasIndex(x => new { x.CompanyId, x.CursorId, x.SourceEventId, x.SourceEventVersion }).IsUnique();
        b.HasIndex(x => new { x.CompanyId, x.Status, x.OccurredUtc });
        b.HasIndex(x => new { x.CompanyId, x.CursorId, x.CreatedUtc });
        b.HasOne(x => x.Cursor).WithMany(x => x.Events).HasForeignKey(x => new { x.CompanyId, x.CursorId })
            .HasPrincipalKey(x => new { x.CompanyId, x.Id }).OnDelete(DeleteBehavior.Cascade);
        b.HasOne(x => x.Run).WithMany().HasForeignKey(x => x.RunId).OnDelete(DeleteBehavior.NoAction);
    }
}
