using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using VirtualCompany.Domain.Entities;

namespace VirtualCompany.Infrastructure.Persistence.Configurations;

public sealed class AccountingProviderSwitchPreparationConfiguration : IEntityTypeConfiguration<AccountingProviderSwitchPreparation>
{
    public void Configure(EntityTypeBuilder<AccountingProviderSwitchPreparation> b)
    {
        b.ToTable("accounting_provider_switch_preparations", t =>
        {
            t.HasCheckConstraint("CK_accounting_provider_switch_preparations_status", "[status] IN ('queued','running','completed','failed')");
            t.HasCheckConstraint("CK_accounting_provider_switch_preparations_counts", "[completed_work_items] >= 0 AND [total_work_items] >= 0 AND [candidate_count] >= 0 AND [valid_candidate_count] >= 0 AND [rejected_candidate_count] >= 0 AND [existing_reference_count] >= 0 AND [archive_dependency_count] >= 0");
            t.HasCheckConstraint("CK_accounting_provider_switch_preparations_version", "[version] > 0");
        });
        b.HasKey(x => x.Id);
        b.HasAlternateKey(x => new { x.CompanyId, x.SwitchId, x.Id });
        b.Property(x => x.Id).HasColumnName("id");
        b.Property(x => x.CompanyId).HasColumnName("company_id");
        b.Property(x => x.SwitchId).HasColumnName("switch_id");
        b.Property(x => x.PlanId).HasColumnName("plan_id");
        b.Property(x => x.PlanHash).HasColumnName("plan_hash").HasMaxLength(64).IsFixedLength();
        b.Property(x => x.Strategy).HasColumnName("strategy").HasMaxLength(48);
        b.Property(x => x.RequestedByUserId).HasColumnName("requested_by_user_id");
        b.Property(x => x.IdempotencyKey).HasColumnName("idempotency_key").HasMaxLength(128);
        b.Property(x => x.CorrelationId).HasColumnName("correlation_id").HasMaxLength(128);
        b.Property(x => x.Status).HasColumnName("status").HasMaxLength(24);
        b.Property(x => x.CompletedWorkItems).HasColumnName("completed_work_items");
        b.Property(x => x.TotalWorkItems).HasColumnName("total_work_items");
        b.Property(x => x.CandidateCount).HasColumnName("candidate_count");
        b.Property(x => x.ValidCandidateCount).HasColumnName("valid_candidate_count");
        b.Property(x => x.RejectedCandidateCount).HasColumnName("rejected_candidate_count");
        b.Property(x => x.ExistingReferenceCount).HasColumnName("existing_reference_count");
        b.Property(x => x.ArchiveDependencyCount).HasColumnName("archive_dependency_count");
        b.Property(x => x.AttemptCount).HasColumnName("attempt_count");
        b.Property(x => x.NextAttemptUtc).HasColumnName("next_attempt_at");
        b.Property(x => x.LeaseOwner).HasColumnName("lease_owner").HasMaxLength(128);
        b.Property(x => x.LeaseExpiresUtc).HasColumnName("lease_expires_at");
        b.Property(x => x.FailureCode).HasColumnName("failure_code").HasMaxLength(100);
        b.Property(x => x.FailureSummary).HasColumnName("failure_summary").HasMaxLength(1000);
        b.Property(x => x.RequestedUtc).HasColumnName("requested_at");
        b.Property(x => x.StartedUtc).HasColumnName("started_at");
        b.Property(x => x.CompletedUtc).HasColumnName("completed_at");
        b.Property(x => x.Version).HasColumnName("version").IsConcurrencyToken();
        b.HasIndex(x => new { x.CompanyId, x.SwitchId, x.IdempotencyKey }).IsUnique();
        b.HasIndex(x => new { x.CompanyId, x.SwitchId, x.PlanHash }).IsUnique();
        b.HasIndex(x => new { x.Status, x.NextAttemptUtc, x.LeaseExpiresUtc });
        b.HasOne(x => x.Company).WithMany().HasForeignKey(x => x.CompanyId).OnDelete(DeleteBehavior.NoAction);
        b.HasOne(x => x.Switch).WithMany().HasForeignKey(x => new { x.CompanyId, x.SwitchId })
            .HasPrincipalKey(x => new { x.CompanyId, x.Id }).OnDelete(DeleteBehavior.Cascade);
        b.HasOne(x => x.Plan).WithMany().HasForeignKey(x => new { x.CompanyId, x.SwitchId, x.PlanId })
            .HasPrincipalKey(x => new { x.CompanyId, x.SwitchId, x.Id }).OnDelete(DeleteBehavior.NoAction);
    }
}

public sealed class AccountingProviderSwitchReadinessCheckConfiguration : IEntityTypeConfiguration<AccountingProviderSwitchReadinessCheck>
{
    public void Configure(EntityTypeBuilder<AccountingProviderSwitchReadinessCheck> b)
    {
        b.ToTable("accounting_provider_switch_readiness_checks");
        Columns(b);
        b.Property(x => x.CheckKey).HasColumnName("check_key").HasMaxLength(80);
        b.Property(x => x.IsReady).HasColumnName("is_ready");
        b.Property(x => x.IsBlocking).HasColumnName("is_blocking");
        b.Property(x => x.ReasonCode).HasColumnName("reason_code").HasMaxLength(100);
        b.Property(x => x.Explanation).HasColumnName("explanation").HasMaxLength(1000);
        b.Property(x => x.EvidenceJson).HasColumnName("evidence_json").HasMaxLength(16000);
        b.Property(x => x.CalculatedUtc).HasColumnName("calculated_at");
        b.HasIndex(x => new { x.CompanyId, x.SwitchId, x.PreparationId, x.CheckKey }).IsUnique();
        LinkRun(b);
    }

    internal static void Columns<T>(EntityTypeBuilder<T> b) where T : class
    {
        b.HasKey("Id");
        b.Property<Guid>("Id").HasColumnName("id");
        b.Property<Guid>("CompanyId").HasColumnName("company_id");
        b.Property<Guid>("SwitchId").HasColumnName("switch_id");
        b.Property<Guid>("PreparationId").HasColumnName("preparation_id");
    }

    internal static void LinkRun<T>(EntityTypeBuilder<T> b) where T : class =>
        b.HasOne<AccountingProviderSwitchPreparation>().WithMany()
            .HasForeignKey("CompanyId", "SwitchId", "PreparationId")
            .HasPrincipalKey(x => new { x.CompanyId, x.SwitchId, x.Id })
            .OnDelete(DeleteBehavior.Cascade);
}

public sealed class AccountingProviderSwitchNativeCandidateConfiguration : IEntityTypeConfiguration<AccountingProviderSwitchNativeCandidate>
{
    public void Configure(EntityTypeBuilder<AccountingProviderSwitchNativeCandidate> b)
    {
        b.ToTable("accounting_provider_switch_native_candidates", t =>
        {
            t.HasCheckConstraint("CK_accounting_provider_switch_native_candidates_status", "[status] IN ('valid','rejected')");
        });
        b.HasKey(x => x.Id);
        b.HasAlternateKey(x => new { x.CompanyId, x.SwitchId, x.Id });
        b.Property(x => x.Id).HasColumnName("id");
        b.Property(x => x.CompanyId).HasColumnName("company_id");
        b.Property(x => x.SwitchId).HasColumnName("switch_id");
        b.Property(x => x.PreparedByRunId).HasColumnName("prepared_by_run_id");
        b.Property(x => x.StagedRecordId).HasColumnName("staged_record_id");
        b.Property(x => x.CandidateKind).HasColumnName("candidate_kind").HasMaxLength(48);
        b.Property(x => x.SourceDataset).HasColumnName("source_dataset").HasMaxLength(64);
        b.Property(x => x.SourceIdentity).HasColumnName("source_identity").HasMaxLength(256);
        b.Property(x => x.SourceVersion).HasColumnName("source_version").HasMaxLength(128);
        b.Property(x => x.SourceHash).HasColumnName("source_hash").HasMaxLength(64).IsFixedLength();
        b.Property(x => x.IdempotencyKey).HasColumnName("idempotency_key").HasMaxLength(200);
        b.Property(x => x.FiscalPeriodId).HasColumnName("fiscal_period_id");
        b.Property(x => x.DocumentDate).HasColumnName("document_date");
        b.Property(x => x.PostingDate).HasColumnName("posting_date");
        b.Property(x => x.FinancialAmount).HasColumnName("financial_amount").HasPrecision(19, 4);
        b.Property(x => x.Currency).HasColumnName("currency").HasMaxLength(3);
        b.Property(x => x.Status).HasColumnName("status").HasMaxLength(24);
        b.Property(x => x.PayloadJson).HasColumnName("payload_json").HasColumnType("nvarchar(max)");
        b.Property(x => x.EvidenceHash).HasColumnName("evidence_hash").HasMaxLength(64).IsFixedLength();
        b.Property(x => x.ExternalReferenceId).HasColumnName("external_reference_id");
        b.Property(x => x.CreatedUtc).HasColumnName("created_at");
        b.Property(x => x.UpdatedUtc).HasColumnName("updated_at");
        b.HasIndex(x => new { x.CompanyId, x.SwitchId, x.StagedRecordId, x.CandidateKind }).IsUnique();
        b.HasIndex(x => new { x.CompanyId, x.SwitchId, x.IdempotencyKey }).IsUnique();
        b.HasIndex(x => new { x.CompanyId, x.SwitchId, x.Status, x.CandidateKind });
        b.HasOne<AccountingProviderSwitchPreparation>().WithMany()
            .HasForeignKey(x => new { x.CompanyId, x.SwitchId, x.PreparedByRunId })
            .HasPrincipalKey(x => new { x.CompanyId, x.SwitchId, x.Id }).OnDelete(DeleteBehavior.NoAction);
        b.HasOne<AccountingProviderSwitchStagedRecord>().WithMany()
            .HasForeignKey(x => new { x.CompanyId, x.SwitchId, x.StagedRecordId })
            .HasPrincipalKey(x => new { x.CompanyId, x.SwitchId, x.Id }).OnDelete(DeleteBehavior.NoAction);
    }
}

public sealed class AccountingProviderSwitchCandidateValidationConfiguration : IEntityTypeConfiguration<AccountingProviderSwitchCandidateValidation>
{
    public void Configure(EntityTypeBuilder<AccountingProviderSwitchCandidateValidation> b)
    {
        b.ToTable("accounting_provider_switch_candidate_validations");
        b.HasKey(x => x.Id);
        b.Property(x => x.Id).HasColumnName("id");
        b.Property(x => x.CompanyId).HasColumnName("company_id");
        b.Property(x => x.SwitchId).HasColumnName("switch_id");
        b.Property(x => x.CandidateId).HasColumnName("candidate_id");
        b.Property(x => x.ReasonCode).HasColumnName("reason_code").HasMaxLength(100);
        b.Property(x => x.IsBlocking).HasColumnName("is_blocking");
        b.Property(x => x.Explanation).HasColumnName("explanation").HasMaxLength(1000);
        b.Property(x => x.EvidenceJson).HasColumnName("evidence_json").HasMaxLength(16000);
        b.Property(x => x.ValidatedUtc).HasColumnName("validated_at");
        b.HasIndex(x => new { x.CompanyId, x.SwitchId, x.CandidateId, x.ReasonCode }).IsUnique();
        b.HasOne<AccountingProviderSwitchNativeCandidate>().WithMany()
            .HasForeignKey(x => new { x.CompanyId, x.SwitchId, x.CandidateId })
            .HasPrincipalKey(x => new { x.CompanyId, x.SwitchId, x.Id }).OnDelete(DeleteBehavior.Cascade);
    }
}

public sealed class AccountingProviderSwitchArchiveDependencyConfiguration : IEntityTypeConfiguration<AccountingProviderSwitchArchiveDependency>
{
    public void Configure(EntityTypeBuilder<AccountingProviderSwitchArchiveDependency> b)
    {
        b.ToTable("accounting_provider_switch_archive_dependencies");
        b.HasKey(x => x.Id);
        b.Property(x => x.Id).HasColumnName("id");
        b.Property(x => x.CompanyId).HasColumnName("company_id");
        b.Property(x => x.SwitchId).HasColumnName("switch_id");
        b.Property(x => x.PreparedByRunId).HasColumnName("prepared_by_run_id");
        b.Property(x => x.StagedRecordId).HasColumnName("staged_record_id");
        b.Property(x => x.Dataset).HasColumnName("dataset").HasMaxLength(64);
        b.Property(x => x.SourceIdentity).HasColumnName("source_identity").HasMaxLength(256);
        b.Property(x => x.ReasonCode).HasColumnName("reason_code").HasMaxLength(100);
        b.Property(x => x.Explanation).HasColumnName("explanation").HasMaxLength(1000);
        b.Property(x => x.EvidenceHash).HasColumnName("evidence_hash").HasMaxLength(64).IsFixedLength();
        b.Property(x => x.ApprovedPlanId).HasColumnName("approved_plan_id");
        b.Property(x => x.ApprovedPlanHash).HasColumnName("approved_plan_hash").HasMaxLength(64).IsFixedLength();
        b.Property(x => x.CreatedUtc).HasColumnName("created_at");
        b.HasIndex(x => new { x.CompanyId, x.SwitchId, x.Dataset, x.SourceIdentity, x.ReasonCode }).IsUnique();
        b.HasOne<AccountingProviderSwitchPreparation>().WithMany()
            .HasForeignKey(x => new { x.CompanyId, x.SwitchId, x.PreparedByRunId })
            .HasPrincipalKey(x => new { x.CompanyId, x.SwitchId, x.Id }).OnDelete(DeleteBehavior.NoAction);
    }
}
