using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using VirtualCompany.Domain.Entities;

namespace VirtualCompany.Infrastructure.Persistence.Configurations;

public sealed class AccountingProviderSwitchRehearsalConfiguration : IEntityTypeConfiguration<AccountingProviderSwitchRehearsal>
{
    public void Configure(EntityTypeBuilder<AccountingProviderSwitchRehearsal> b)
    {
        b.ToTable("accounting_provider_switch_rehearsals", t =>
        {
            t.HasCheckConstraint("CK_accounting_provider_switch_rehearsals_status", "[status] IN ('queued','running','completed','failed')");
            t.HasCheckConstraint("CK_accounting_provider_switch_rehearsals_version", "[version] > 0");
        });
        b.HasKey(x => x.Id); b.HasAlternateKey(x => new { x.CompanyId, x.SwitchId, x.Id });
        b.Property(x => x.Id).HasColumnName("id"); b.Property(x => x.CompanyId).HasColumnName("company_id");
        b.Property(x => x.SwitchId).HasColumnName("switch_id"); b.Property(x => x.RequestedByUserId).HasColumnName("requested_by_user_id");
        b.Property(x => x.IdempotencyKey).HasColumnName("idempotency_key").HasMaxLength(128);
        b.Property(x => x.CorrelationId).HasColumnName("correlation_id").HasMaxLength(128);
        b.Property(x => x.Status).HasColumnName("status").HasMaxLength(24); b.Property(x => x.SimulationKind).HasColumnName("simulation_kind").HasMaxLength(32);
        b.Property(x => x.ProviderAcceptanceProven).HasColumnName("provider_acceptance_proven"); b.Property(x => x.Disclosure).HasColumnName("disclosure").HasMaxLength(1000);
        b.Property(x => x.CompletedWorkItems).HasColumnName("completed_work_items"); b.Property(x => x.TotalWorkItems).HasColumnName("total_work_items");
        b.Property(x => x.AttemptCount).HasColumnName("attempt_count"); b.Property(x => x.NextAttemptUtc).HasColumnName("next_attempt_at");
        b.Property(x => x.LeaseOwner).HasColumnName("lease_owner").HasMaxLength(128); b.Property(x => x.LeaseExpiresUtc).HasColumnName("lease_expires_at");
        b.Property(x => x.FailureCode).HasColumnName("failure_code").HasMaxLength(100); b.Property(x => x.FailureSummary).HasColumnName("failure_summary").HasMaxLength(1000);
        b.Property(x => x.RequestedUtc).HasColumnName("requested_at"); b.Property(x => x.StartedUtc).HasColumnName("started_at"); b.Property(x => x.CompletedUtc).HasColumnName("completed_at");
        b.Property(x => x.Version).HasColumnName("version").IsConcurrencyToken();
        b.HasIndex(x => new { x.CompanyId, x.SwitchId, x.IdempotencyKey }).IsUnique();
        b.HasIndex(x => new { x.Status, x.NextAttemptUtc, x.LeaseExpiresUtc });
        b.HasOne(x => x.Company).WithMany().HasForeignKey(x => x.CompanyId).OnDelete(DeleteBehavior.NoAction);
        b.HasOne(x => x.Switch).WithMany().HasForeignKey(x => new { x.CompanyId, x.SwitchId }).HasPrincipalKey(x => new { x.CompanyId, x.Id }).OnDelete(DeleteBehavior.Cascade);
    }
}

public sealed class AccountingProviderSwitchRehearsalInputConfiguration : IEntityTypeConfiguration<AccountingProviderSwitchRehearsalInput>
{
    public void Configure(EntityTypeBuilder<AccountingProviderSwitchRehearsalInput> b)
    {
        b.ToTable("accounting_provider_switch_rehearsal_inputs"); b.HasKey(x => x.Id);
        Columns(b); b.Property(x => x.SwitchVersion).HasColumnName("switch_version"); b.Property(x => x.Strategy).HasColumnName("strategy").HasMaxLength(48);
        b.Property(x => x.SourceSnapshotHash).HasColumnName("source_snapshot_hash").HasMaxLength(64).IsFixedLength();
        b.Property(x => x.StagingHash).HasColumnName("staging_hash").HasMaxLength(64).IsFixedLength(); b.Property(x => x.MappingHash).HasColumnName("mapping_hash").HasMaxLength(64).IsFixedLength();
        b.Property(x => x.GapHash).HasColumnName("gap_hash").HasMaxLength(64).IsFixedLength(); b.Property(x => x.StagedRecordCount).HasColumnName("staged_record_count");
        b.Property(x => x.FinancialTotal).HasColumnName("financial_total").HasPrecision(19, 4); b.Property(x => x.DatasetSummaryJson).HasColumnName("dataset_summary_json").HasMaxLength(16000);
        b.Property(x => x.CreatedUtc).HasColumnName("created_at"); b.HasIndex(x => new { x.CompanyId, x.SwitchId, x.RehearsalId }).IsUnique(); LinkRun(b);
    }
    internal static void Columns<T>(EntityTypeBuilder<T> b) where T : class
    {
        b.Property<Guid>("Id").HasColumnName("id"); b.Property<Guid>("CompanyId").HasColumnName("company_id"); b.Property<Guid>("SwitchId").HasColumnName("switch_id"); b.Property<Guid>("RehearsalId").HasColumnName("rehearsal_id");
    }
    internal static void LinkRun<T>(EntityTypeBuilder<T> b) where T : class => b.HasOne<AccountingProviderSwitchRehearsal>().WithMany().HasForeignKey("CompanyId", "SwitchId", "RehearsalId").HasPrincipalKey(x => new { x.CompanyId, x.SwitchId, x.Id }).OnDelete(DeleteBehavior.Cascade);
}

public sealed class AccountingProviderSwitchRehearsalDatasetResultConfiguration : IEntityTypeConfiguration<AccountingProviderSwitchRehearsalDatasetResult>
{
    public void Configure(EntityTypeBuilder<AccountingProviderSwitchRehearsalDatasetResult> b)
    {
        b.ToTable("accounting_provider_switch_rehearsal_datasets"); b.HasKey(x => x.Id); AccountingProviderSwitchRehearsalInputConfiguration.Columns(b);
        b.Property(x => x.Dataset).HasColumnName("dataset").HasMaxLength(64); b.Property(x => x.ExpectedCount).HasColumnName("expected_count"); b.Property(x => x.ObservedCount).HasColumnName("observed_count");
        b.Property(x => x.ExpectedTotal).HasColumnName("expected_total").HasPrecision(19,4); b.Property(x => x.ObservedTotal).HasColumnName("observed_total").HasPrecision(19,4);
        b.Property(x => x.Currency).HasColumnName("currency").HasMaxLength(16); b.Property(x => x.CurrencyKey).HasColumnName("currency_key").HasMaxLength(16); b.Property(x => x.Result).HasColumnName("result").HasMaxLength(32); b.Property(x => x.ReasonCode).HasColumnName("reason_code").HasMaxLength(100);
        b.Property(x => x.EvidenceJson).HasColumnName("evidence_json").HasMaxLength(16000); b.Property(x => x.CalculatedUtc).HasColumnName("calculated_at");
        b.HasIndex(x => new { x.CompanyId, x.SwitchId, x.RehearsalId, x.Dataset, x.CurrencyKey }).IsUnique(); AccountingProviderSwitchRehearsalInputConfiguration.LinkRun(b);
    }
}

public sealed class AccountingProviderSwitchReconciliationCheckConfiguration : IEntityTypeConfiguration<AccountingProviderSwitchReconciliationCheck>
{
    public void Configure(EntityTypeBuilder<AccountingProviderSwitchReconciliationCheck> b)
    {
        b.ToTable("accounting_provider_switch_reconciliation_checks"); b.HasKey(x => x.Id); AccountingProviderSwitchRehearsalInputConfiguration.Columns(b);
        b.Property(x => x.CheckKey).HasColumnName("check_key").HasMaxLength(80); b.Property(x => x.ExpectedValue).HasColumnName("expected_value").HasMaxLength(1000); b.Property(x => x.ObservedValue).HasColumnName("observed_value").HasMaxLength(1000);
        b.Property(x => x.Tolerance).HasColumnName("tolerance").HasPrecision(19,4); b.Property(x => x.Currency).HasColumnName("currency").HasMaxLength(16); b.Property(x => x.CurrencyKey).HasColumnName("currency_key").HasMaxLength(16); b.Property(x => x.Result).HasColumnName("result").HasMaxLength(32);
        b.Property(x => x.ReasonCode).HasColumnName("reason_code").HasMaxLength(100); b.Property(x => x.DataSourcesJson).HasColumnName("data_sources_json").HasMaxLength(16000);
        b.Property(x => x.CalculationVersion).HasColumnName("calculation_version").HasMaxLength(32); b.Property(x => x.ManualEvidenceAllowed).HasColumnName("manual_evidence_allowed"); b.Property(x => x.CalculatedUtc).HasColumnName("calculated_at");
        b.HasIndex(x => new { x.CompanyId, x.SwitchId, x.RehearsalId, x.CheckKey, x.CurrencyKey }).IsUnique(); AccountingProviderSwitchRehearsalInputConfiguration.LinkRun(b);
    }
}

public sealed class AccountingProviderSwitchManualEvidenceConfiguration : IEntityTypeConfiguration<AccountingProviderSwitchManualEvidence>
{
    public void Configure(EntityTypeBuilder<AccountingProviderSwitchManualEvidence> b)
    {
        b.ToTable("accounting_provider_switch_manual_evidence"); b.HasKey(x => x.Id); AccountingProviderSwitchRehearsalInputConfiguration.Columns(b);
        b.Property(x => x.CheckId).HasColumnName("check_id"); b.Property(x => x.InputHash).HasColumnName("input_hash").HasMaxLength(64).IsFixedLength(); b.Property(x => x.Explanation).HasColumnName("explanation").HasMaxLength(1000);
        b.Property(x => x.EvidenceReference).HasColumnName("evidence_reference").HasMaxLength(1000); b.Property(x => x.RecordedByUserId).HasColumnName("recorded_by_user_id"); b.Property(x => x.RecordedUtc).HasColumnName("recorded_at"); b.Property(x => x.ExpiresUtc).HasColumnName("expires_at");
        b.HasIndex(x => new { x.CompanyId, x.SwitchId, x.RehearsalId, x.CheckId, x.InputHash }).IsUnique(); AccountingProviderSwitchRehearsalInputConfiguration.LinkRun(b);
        b.HasOne<AccountingProviderSwitchReconciliationCheck>().WithMany().HasForeignKey(x => x.CheckId).OnDelete(DeleteBehavior.NoAction);
    }
}

public sealed class AccountingProviderSwitchCutoverPlanConfiguration : IEntityTypeConfiguration<AccountingProviderSwitchCutoverPlan>
{
    public void Configure(EntityTypeBuilder<AccountingProviderSwitchCutoverPlan> b)
    {
        b.ToTable("accounting_provider_switch_cutover_plans"); b.HasKey(x => x.Id); b.HasAlternateKey(x => new { x.CompanyId, x.SwitchId, x.Id }); b.Property(x => x.Id).HasColumnName("id"); b.Property(x => x.CompanyId).HasColumnName("company_id"); b.Property(x => x.SwitchId).HasColumnName("switch_id"); b.Property(x => x.RehearsalId).HasColumnName("rehearsal_id");
        b.Property(x => x.PlanVersion).HasColumnName("plan_version"); b.Property(x => x.PlanHash).HasColumnName("plan_hash").HasMaxLength(64).IsFixedLength(); b.Property(x => x.SourceSnapshotHash).HasColumnName("source_snapshot_hash").HasMaxLength(64).IsFixedLength();
        b.Property(x => x.Strategy).HasColumnName("strategy").HasMaxLength(48); b.Property(x => x.FreezeStartsUtc).HasColumnName("freeze_starts_at"); b.Property(x => x.FreezeEndsUtc).HasColumnName("freeze_ends_at"); b.Property(x => x.RecoveryBoundary).HasColumnName("recovery_boundary").HasMaxLength(1000);
        b.Property(x => x.ParticipantsJson).HasColumnName("participants_json").HasMaxLength(8000); b.Property(x => x.SnapshotJson).HasColumnName("snapshot_json").HasMaxLength(32000); b.Property(x => x.GeneratedByUserId).HasColumnName("generated_by_user_id"); b.Property(x => x.GeneratedUtc).HasColumnName("generated_at");
        b.HasIndex(x => new { x.CompanyId, x.SwitchId, x.PlanVersion }).IsUnique(); b.HasIndex(x => new { x.CompanyId, x.SwitchId, x.PlanHash }).IsUnique();
        b.HasOne<AccountingProviderSwitch>().WithMany().HasForeignKey(x => new { x.CompanyId, x.SwitchId }).HasPrincipalKey(x => new { x.CompanyId, x.Id }).OnDelete(DeleteBehavior.Cascade);
        b.HasOne<AccountingProviderSwitchRehearsal>().WithMany().HasForeignKey(x => new { x.CompanyId, x.SwitchId, x.RehearsalId }).HasPrincipalKey(x => new { x.CompanyId, x.SwitchId, x.Id }).OnDelete(DeleteBehavior.NoAction);
    }
}

public sealed class AccountingProviderSwitchPlanApprovalConfiguration : IEntityTypeConfiguration<AccountingProviderSwitchPlanApproval>
{
    public void Configure(EntityTypeBuilder<AccountingProviderSwitchPlanApproval> b)
    {
        b.ToTable("accounting_provider_switch_plan_approvals"); b.HasKey(x => x.Id); b.Property(x => x.Id).HasColumnName("id"); b.Property(x => x.CompanyId).HasColumnName("company_id"); b.Property(x => x.SwitchId).HasColumnName("switch_id"); b.Property(x => x.PlanId).HasColumnName("plan_id");
        b.Property(x => x.PlanHash).HasColumnName("plan_hash").HasMaxLength(64).IsFixedLength(); b.Property(x => x.ApprovalRequestId).HasColumnName("approval_request_id"); b.Property(x => x.RequestedByUserId).HasColumnName("requested_by_user_id"); b.Property(x => x.RequestedUtc).HasColumnName("requested_at");
        b.HasIndex(x => new { x.CompanyId, x.PlanId }).IsUnique(); b.HasIndex(x => new { x.CompanyId, x.ApprovalRequestId }).IsUnique();
        b.HasOne<AccountingProviderSwitchCutoverPlan>().WithMany().HasForeignKey(x => new { x.CompanyId, x.SwitchId, x.PlanId }).HasPrincipalKey(x => new { x.CompanyId, x.SwitchId, x.Id }).OnDelete(DeleteBehavior.Cascade);
        b.HasOne<ApprovalRequest>().WithMany().HasForeignKey(x => x.ApprovalRequestId).OnDelete(DeleteBehavior.NoAction);
    }
}
