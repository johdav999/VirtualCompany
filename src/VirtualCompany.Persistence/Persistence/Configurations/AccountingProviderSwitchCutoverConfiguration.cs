using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using VirtualCompany.Domain.Entities;

namespace VirtualCompany.Infrastructure.Persistence.Configurations;

public sealed class AccountingProviderSwitchCutoverExecutionConfiguration : IEntityTypeConfiguration<AccountingProviderSwitchCutoverExecution>
{
    public void Configure(EntityTypeBuilder<AccountingProviderSwitchCutoverExecution> b)
    {
        b.ToTable("accounting_provider_switch_cutover_executions", table =>
        {
            table.HasCheckConstraint("ck_accounting_provider_switch_cutover_executions_status",
                "[status] IN ('queued','freezing','transferring','reconciling','awaiting_activation_approval','activating','activated','blocked','cancelled','recovered','corrective_cutover_required')");
            table.HasCheckConstraint("ck_accounting_provider_switch_cutover_executions_version", "[version] >= 1");
            table.HasCheckConstraint("ck_accounting_provider_switch_cutover_executions_attempt_count", "[attempt_count] >= 0");
        });
        b.HasKey(x => x.Id);
        b.HasAlternateKey(x => new { x.CompanyId, x.SwitchId, x.Id });
        b.Property(x => x.Id).HasColumnName("id"); b.Property(x => x.CompanyId).HasColumnName("company_id");
        b.Property(x => x.SwitchId).HasColumnName("switch_id"); b.Property(x => x.PlanId).HasColumnName("plan_id");
        b.Property(x => x.PlanVersion).HasColumnName("plan_version"); b.Property(x => x.PlanHash).HasColumnName("plan_hash").HasMaxLength(64).IsFixedLength();
        b.Property(x => x.PreparationId).HasColumnName("preparation_id"); b.Property(x => x.TargetTransferBatchId).HasColumnName("target_transfer_batch_id");
        b.Property(x => x.FinalSnapshotId).HasColumnName("final_snapshot_id"); b.Property(x => x.AuthorityPeriodId).HasColumnName("authority_period_id");
        b.Property(x => x.RequestedByUserId).HasColumnName("requested_by_user_id"); b.Property(x => x.IdempotencyKey).HasColumnName("idempotency_key").HasMaxLength(128);
        b.Property(x => x.CorrelationId).HasColumnName("correlation_id").HasMaxLength(128); b.Property(x => x.Status).HasColumnName("status").HasMaxLength(48);
        b.Property(x => x.CurrentStep).HasColumnName("current_step").HasMaxLength(80); b.Property(x => x.TargetActivityRecorded).HasColumnName("target_activity_recorded");
        b.Property(x => x.RetryIsSafe).HasColumnName("retry_is_safe"); b.Property(x => x.ProviderReconciliationRequired).HasColumnName("provider_reconciliation_required");
        b.Property(x => x.FailureCode).HasColumnName("failure_code").HasMaxLength(100); b.Property(x => x.FailureSummary).HasColumnName("failure_summary").HasMaxLength(1000);
        b.Property(x => x.NextAction).HasColumnName("next_action").HasMaxLength(1000); b.Property(x => x.AttemptCount).HasColumnName("attempt_count");
        b.Property(x => x.NextAttemptUtc).HasColumnName("next_attempt_at"); b.Property(x => x.LeaseOwner).HasColumnName("lease_owner").HasMaxLength(128);
        b.Property(x => x.LeaseExpiresUtc).HasColumnName("lease_expires_at"); b.Property(x => x.ScheduledUtc).HasColumnName("scheduled_at");
        b.Property(x => x.RequestedUtc).HasColumnName("requested_at"); b.Property(x => x.FreezeStartedUtc).HasColumnName("freeze_started_at");
        b.Property(x => x.ReconciledUtc).HasColumnName("reconciled_at"); b.Property(x => x.ActivatedUtc).HasColumnName("activated_at");
        b.Property(x => x.CompletedUtc).HasColumnName("completed_at"); b.Property(x => x.Version).HasColumnName("version").IsConcurrencyToken();
        b.HasIndex(x => new { x.CompanyId, x.SwitchId, x.IdempotencyKey }).IsUnique();
        b.HasIndex(x => new { x.CompanyId, x.SwitchId }).IsUnique().HasFilter("[status] <> 'activated' AND [status] <> 'cancelled' AND [status] <> 'recovered' AND [status] <> 'corrective_cutover_required'");
        b.HasIndex(x => new { x.Status, x.NextAttemptUtc, x.LeaseExpiresUtc });
        b.HasOne(x => x.Switch).WithMany().HasForeignKey(x => new { x.CompanyId, x.SwitchId }).HasPrincipalKey(x => new { x.CompanyId, x.Id }).OnDelete(DeleteBehavior.Cascade);
        b.HasOne(x => x.Plan).WithMany().HasForeignKey(x => new { x.CompanyId, x.SwitchId, x.PlanId }).HasPrincipalKey(x => new { x.CompanyId, x.SwitchId, x.Id }).OnDelete(DeleteBehavior.NoAction);
        b.HasOne<AccountingProviderSwitchPreparation>().WithMany().HasForeignKey(x => x.PreparationId).OnDelete(DeleteBehavior.NoAction);
        b.HasOne<AccountingProviderSwitchTargetTransferBatch>().WithMany().HasForeignKey(x => x.TargetTransferBatchId).OnDelete(DeleteBehavior.NoAction);
        b.HasOne<AccountingAuthorityPeriod>().WithMany().HasForeignKey(x => x.AuthorityPeriodId).OnDelete(DeleteBehavior.NoAction);
    }
}

public sealed class AccountingProviderSwitchFinalSnapshotConfiguration : IEntityTypeConfiguration<AccountingProviderSwitchFinalSnapshot>
{
    public void Configure(EntityTypeBuilder<AccountingProviderSwitchFinalSnapshot> b)
    {
        b.ToTable("accounting_provider_switch_final_snapshots"); b.HasKey(x => x.Id);
        Columns(b); b.Property(x => x.ApprovedSourceSnapshotHash).HasColumnName("approved_source_snapshot_hash").HasMaxLength(64).IsFixedLength();
        b.Property(x => x.FinalSourceSnapshotHash).HasColumnName("final_source_snapshot_hash").HasMaxLength(64).IsFixedLength();
        b.Property(x => x.StagingHash).HasColumnName("staging_hash").HasMaxLength(64).IsFixedLength(); b.Property(x => x.MappingHash).HasColumnName("mapping_hash").HasMaxLength(64).IsFixedLength();
        b.Property(x => x.GapHash).HasColumnName("gap_hash").HasMaxLength(64).IsFixedLength(); b.Property(x => x.RecordCount).HasColumnName("record_count");
        b.Property(x => x.FinancialTotal).HasColumnName("financial_total").HasPrecision(19,4); b.Property(x => x.DeltaRecordCount).HasColumnName("delta_record_count");
        b.Property(x => x.DeltaFinancialTotal).HasColumnName("delta_financial_total").HasPrecision(19,4); b.Property(x => x.SnapshotJson).HasColumnName("snapshot_json").HasMaxLength(64000);
        b.Property(x => x.ExtractionStartedUtc).HasColumnName("extraction_started_at"); b.Property(x => x.ExtractionCompletedUtc).HasColumnName("extraction_completed_at");
        b.HasIndex(x => new { x.CompanyId, x.SwitchId, x.ExecutionId }).IsUnique(); LinkExecution(b);
    }
    internal static void Columns<T>(EntityTypeBuilder<T> b) where T : class
    { b.Property<Guid>("Id").HasColumnName("id"); b.Property<Guid>("CompanyId").HasColumnName("company_id"); b.Property<Guid>("SwitchId").HasColumnName("switch_id"); b.Property<Guid>("ExecutionId").HasColumnName("execution_id"); }
    internal static void LinkExecution<T>(EntityTypeBuilder<T> b) where T : class => b.HasOne<AccountingProviderSwitchCutoverExecution>().WithMany()
        .HasForeignKey("CompanyId", "SwitchId", "ExecutionId").HasPrincipalKey(x => new { x.CompanyId, x.SwitchId, x.Id }).OnDelete(DeleteBehavior.Cascade);
}

public sealed class AccountingProviderSwitchFinalCheckConfiguration : IEntityTypeConfiguration<AccountingProviderSwitchFinalCheck>
{
    public void Configure(EntityTypeBuilder<AccountingProviderSwitchFinalCheck> b)
    {
        b.ToTable("accounting_provider_switch_final_checks", table => table.HasCheckConstraint(
            "ck_accounting_provider_switch_final_checks_result", "[result] IN ('passed','failed')"));
        b.HasKey(x => x.Id); AccountingProviderSwitchFinalSnapshotConfiguration.Columns(b);
        b.Property(x => x.CheckKey).HasColumnName("check_key").HasMaxLength(80); b.Property(x => x.Result).HasColumnName("result").HasMaxLength(16);
        b.Property(x => x.ReasonCode).HasColumnName("reason_code").HasMaxLength(100); b.Property(x => x.Explanation).HasColumnName("explanation").HasMaxLength(1000);
        b.Property(x => x.EvidenceJson).HasColumnName("evidence_json").HasMaxLength(16000); b.Property(x => x.CalculatedUtc).HasColumnName("calculated_at");
        b.HasIndex(x => new { x.CompanyId, x.SwitchId, x.ExecutionId, x.CheckKey }).IsUnique(); AccountingProviderSwitchFinalSnapshotConfiguration.LinkExecution(b);
    }
}

public sealed class AccountingProviderSwitchActivationApprovalConfiguration : IEntityTypeConfiguration<AccountingProviderSwitchActivationApproval>
{
    public void Configure(EntityTypeBuilder<AccountingProviderSwitchActivationApproval> b)
    {
        b.ToTable("accounting_provider_switch_activation_approvals"); b.HasKey(x => x.Id); AccountingProviderSwitchFinalSnapshotConfiguration.Columns(b);
        b.Property(x => x.FinalSnapshotId).HasColumnName("final_snapshot_id"); b.Property(x => x.FinalSnapshotHash).HasColumnName("final_snapshot_hash").HasMaxLength(64).IsFixedLength();
        b.Property(x => x.ReconciliationHash).HasColumnName("reconciliation_hash").HasMaxLength(64).IsFixedLength(); b.Property(x => x.SwitchVersion).HasColumnName("switch_version");
        b.Property(x => x.ApprovalRequestId).HasColumnName("approval_request_id"); b.Property(x => x.RequestedByUserId).HasColumnName("requested_by_user_id"); b.Property(x => x.RequestedUtc).HasColumnName("requested_at");
        b.HasIndex(x => new { x.CompanyId, x.ExecutionId }).IsUnique(); b.HasIndex(x => new { x.CompanyId, x.ApprovalRequestId }).IsUnique();
        AccountingProviderSwitchFinalSnapshotConfiguration.LinkExecution(b); b.HasOne<ApprovalRequest>().WithMany().HasForeignKey(x => x.ApprovalRequestId).OnDelete(DeleteBehavior.NoAction);
    }
}

public sealed class AccountingProviderSwitchNativeMaterializationConfiguration : IEntityTypeConfiguration<AccountingProviderSwitchNativeMaterialization>
{
    public void Configure(EntityTypeBuilder<AccountingProviderSwitchNativeMaterialization> b)
    {
        b.ToTable("accounting_provider_switch_native_materializations"); b.HasKey(x => x.Id); AccountingProviderSwitchFinalSnapshotConfiguration.Columns(b);
        b.Property(x => x.CandidateId).HasColumnName("candidate_id"); b.Property(x => x.CandidateHash).HasColumnName("candidate_hash").HasMaxLength(64).IsFixedLength();
        b.Property(x => x.TargetRecordId).HasColumnName("target_record_id"); b.Property(x => x.TargetRecordType).HasColumnName("target_record_type").HasMaxLength(64);
        b.Property(x => x.MaterializedUtc).HasColumnName("materialized_at"); b.HasIndex(x => new { x.CompanyId, x.SwitchId, x.CandidateId }).IsUnique();
        AccountingProviderSwitchFinalSnapshotConfiguration.LinkExecution(b); b.HasOne<AccountingProviderSwitchNativeCandidate>().WithMany().HasForeignKey(x => x.CandidateId).OnDelete(DeleteBehavior.NoAction);
    }
}
