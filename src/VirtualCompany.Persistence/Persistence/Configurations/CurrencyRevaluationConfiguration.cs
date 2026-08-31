using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using VirtualCompany.Domain.Entities;

namespace VirtualCompany.Infrastructure.Persistence.Configurations;

public sealed class CurrencyRevaluationRunConfiguration : IEntityTypeConfiguration<CurrencyRevaluationRun>
{
    public void Configure(EntityTypeBuilder<CurrencyRevaluationRun> builder)
    {
        builder.ToTable("currency_revaluation_runs", table =>
        {
            table.HasCheckConstraint("CK_currency_revaluation_runs_status",
                "status IN ('draft','needs_review','awaiting_approval','posted','reversed','superseded','failed')");
            table.HasCheckConstraint("CK_currency_revaluation_runs_counts",
                "population_count >= 0 AND included_count >= 0 AND excluded_count >= 0 AND review_count >= 0");
        });
        builder.HasKey(x => x.Id);
        builder.HasAlternateKey(x => new { x.CompanyId, x.Id });
        builder.Property(x => x.Id).HasColumnName("id");
        builder.Property(x => x.CompanyId).HasColumnName("company_id");
        builder.Property(x => x.FiscalPeriodId).HasColumnName("fiscal_period_id");
        builder.Property(x => x.RunNumber).HasColumnName("run_number");
        builder.Property(x => x.AsOfDate).HasColumnName("as_of_date");
        builder.Property(x => x.FunctionalCurrency).HasColumnName("functional_currency").HasMaxLength(3);
        builder.Property(x => x.VoucherSeriesCode).HasColumnName("voucher_series_code").HasMaxLength(32);
        builder.Property(x => x.RequestIdentity).HasColumnName("request_identity").HasMaxLength(200);
        builder.Property(x => x.Status).HasColumnName("status").HasMaxLength(32);
        builder.Property(x => x.FailureReasonCode).HasColumnName("failure_reason_code").HasMaxLength(96);
        builder.Property(x => x.FailureSummary).HasColumnName("failure_summary").HasMaxLength(1000);
        builder.Property(x => x.PopulationChecksum).HasColumnName("population_checksum").HasMaxLength(64);
        builder.Property(x => x.RateSetChecksum).HasColumnName("rate_set_checksum").HasMaxLength(64);
        builder.Property(x => x.ProposalChecksum).HasColumnName("proposal_checksum").HasMaxLength(64);
        builder.Property(x => x.PopulationCount).HasColumnName("population_count");
        builder.Property(x => x.IncludedCount).HasColumnName("included_count");
        builder.Property(x => x.ExcludedCount).HasColumnName("excluded_count");
        builder.Property(x => x.ReviewCount).HasColumnName("review_count");
        builder.Property(x => x.DocumentBalanceTotal).HasColumnName("document_balance_total").HasPrecision(38, 18);
        builder.Property(x => x.CarryingFunctionalTotal).HasColumnName("carrying_functional_total").HasPrecision(38, 18);
        builder.Property(x => x.RevaluedFunctionalTotal).HasColumnName("revalued_functional_total").HasPrecision(38, 18);
        builder.Property(x => x.ProposedAdjustmentTotal).HasColumnName("proposed_adjustment_total").HasPrecision(38, 18);
        builder.Property(x => x.ApprovalRequestId).HasColumnName("approval_request_id");
        builder.Property(x => x.LedgerEntryId).HasColumnName("ledger_entry_id");
        builder.Property(x => x.ReversalLedgerEntryId).HasColumnName("reversal_ledger_entry_id");
        builder.Property(x => x.SupersededByRunId).HasColumnName("superseded_by_run_id");
        builder.Property(x => x.IsScheduled).HasColumnName("is_scheduled");
        builder.Property(x => x.CreatedByUserId).HasColumnName("created_by_user_id");
        builder.Property(x => x.PostedByUserId).HasColumnName("posted_by_user_id");
        builder.Property(x => x.ReversedByUserId).HasColumnName("reversed_by_user_id");
        builder.Property(x => x.CreatedUtc).HasColumnName("created_utc");
        builder.Property(x => x.UpdatedUtc).HasColumnName("updated_utc");
        builder.Property(x => x.SubmittedUtc).HasColumnName("submitted_utc");
        builder.Property(x => x.PostedUtc).HasColumnName("posted_utc");
        builder.Property(x => x.ReversedUtc).HasColumnName("reversed_utc");
        builder.Property(x => x.Version).HasColumnName("version").IsConcurrencyToken();
        builder.HasIndex(x => new { x.CompanyId, x.FiscalPeriodId, x.RunNumber }).IsUnique();
        builder.HasIndex(x => new { x.CompanyId, x.RequestIdentity }).IsUnique();
        builder.HasIndex(x => new { x.CompanyId, x.FiscalPeriodId, x.Status, x.UpdatedUtc });
        builder.HasIndex(x => new { x.CompanyId, x.LedgerEntryId }).IsUnique().HasFilter("[ledger_entry_id] IS NOT NULL");
        builder.HasIndex(x => new { x.CompanyId, x.ReversalLedgerEntryId }).IsUnique().HasFilter("[reversal_ledger_entry_id] IS NOT NULL");
        builder.HasOne(x => x.FiscalPeriod).WithMany().HasForeignKey(x => new { x.CompanyId, x.FiscalPeriodId })
            .HasPrincipalKey(x => new { x.CompanyId, x.Id }).OnDelete(DeleteBehavior.Restrict);
        builder.HasOne(x => x.ApprovalRequest).WithMany().HasForeignKey(x => new { x.CompanyId, x.ApprovalRequestId })
            .HasPrincipalKey(x => new { x.CompanyId, x.Id }).OnDelete(DeleteBehavior.Restrict);
        builder.HasOne(x => x.LedgerEntry).WithMany().HasForeignKey(x => new { x.CompanyId, x.LedgerEntryId })
            .HasPrincipalKey(x => new { x.CompanyId, x.Id }).OnDelete(DeleteBehavior.Restrict);
        builder.HasOne(x => x.ReversalLedgerEntry).WithMany().HasForeignKey(x => new { x.CompanyId, x.ReversalLedgerEntryId })
            .HasPrincipalKey(x => new { x.CompanyId, x.Id }).OnDelete(DeleteBehavior.Restrict);
        builder.HasOne(x => x.SupersededByRun).WithMany().HasForeignKey(x => new { x.CompanyId, x.SupersededByRunId })
            .HasPrincipalKey(x => new { x.CompanyId, x.Id }).OnDelete(DeleteBehavior.Restrict);
    }
}

public sealed class CurrencyRevaluationPopulationItemConfiguration : IEntityTypeConfiguration<CurrencyRevaluationPopulationItem>
{
    public void Configure(EntityTypeBuilder<CurrencyRevaluationPopulationItem> builder)
    {
        builder.ToTable("currency_revaluation_population_items", table => table.HasCheckConstraint(
            "CK_currency_revaluation_population_status", "status IN ('included','excluded','needs_review')"));
        builder.HasKey(x => x.Id); builder.HasAlternateKey(x => new { x.CompanyId, x.Id });
        builder.Property(x => x.Id).HasColumnName("id"); builder.Property(x => x.CompanyId).HasColumnName("company_id"); builder.Property(x => x.RunId).HasColumnName("run_id");
        builder.Property(x => x.PopulationKey).HasColumnName("population_key").HasMaxLength(200); builder.Property(x => x.MonetaryClass).HasColumnName("monetary_class").HasMaxLength(32);
        builder.Property(x => x.FinanceAccountId).HasColumnName("finance_account_id"); builder.Property(x => x.AccountCode).HasColumnName("account_code").HasMaxLength(32); builder.Property(x => x.AccountName).HasColumnName("account_name").HasMaxLength(160);
        builder.Property(x => x.NormalBalance).HasColumnName("normal_balance").HasMaxLength(16); builder.Property(x => x.DocumentCurrency).HasColumnName("document_currency").HasMaxLength(3); builder.Property(x => x.FunctionalCurrency).HasColumnName("functional_currency").HasMaxLength(3);
        builder.Property(x => x.DocumentBalance).HasColumnName("document_balance").HasPrecision(38, 18); builder.Property(x => x.CarryingFunctionalAmount).HasColumnName("carrying_functional_amount").HasPrecision(38, 18); builder.Property(x => x.RevaluedFunctionalAmount).HasColumnName("revalued_functional_amount").HasPrecision(38, 18); builder.Property(x => x.AdjustmentAmount).HasColumnName("adjustment_amount").HasPrecision(38, 18);
        builder.Property(x => x.ExchangeRateConversionId).HasColumnName("exchange_rate_conversion_id"); builder.Property(x => x.PeriodEndRate).HasColumnName("period_end_rate").HasPrecision(38, 18); builder.Property(x => x.RateDate).HasColumnName("rate_date");
        builder.Property(x => x.SourceChecksum).HasColumnName("source_checksum").HasMaxLength(64); builder.Property(x => x.Status).HasColumnName("status").HasMaxLength(32); builder.Property(x => x.ReviewReason).HasColumnName("review_reason").HasMaxLength(1000);
        builder.HasIndex(x => new { x.CompanyId, x.RunId, x.PopulationKey }).IsUnique(); builder.HasIndex(x => new { x.CompanyId, x.FinanceAccountId, x.DocumentCurrency }); builder.HasIndex(x => new { x.CompanyId, x.Status });
        builder.HasOne(x => x.Run).WithMany(x => x.PopulationItems).HasForeignKey(x => new { x.CompanyId, x.RunId }).HasPrincipalKey(x => new { x.CompanyId, x.Id }).OnDelete(DeleteBehavior.Cascade);
        builder.HasOne(x => x.FinanceAccount).WithMany().HasForeignKey(x => new { x.CompanyId, x.FinanceAccountId }).HasPrincipalKey(x => new { x.CompanyId, x.Id }).OnDelete(DeleteBehavior.Restrict);
        builder.HasOne(x => x.ExchangeRateConversion).WithMany().HasForeignKey(x => new { x.CompanyId, x.ExchangeRateConversionId }).HasPrincipalKey(x => new { x.CompanyId, x.Id }).OnDelete(DeleteBehavior.Restrict);
    }
}

public sealed class CurrencyRevaluationRateBindingConfiguration : IEntityTypeConfiguration<CurrencyRevaluationRateBinding>
{
    public void Configure(EntityTypeBuilder<CurrencyRevaluationRateBinding> builder)
    {
        builder.ToTable("currency_revaluation_rate_bindings"); builder.HasKey(x => x.Id);
        builder.Property(x => x.Id).HasColumnName("id"); builder.Property(x => x.CompanyId).HasColumnName("company_id"); builder.Property(x => x.RunId).HasColumnName("run_id"); builder.Property(x => x.PopulationItemId).HasColumnName("population_item_id"); builder.Property(x => x.ExchangeRateConversionId).HasColumnName("exchange_rate_conversion_id");
        builder.Property(x => x.DocumentCurrency).HasColumnName("document_currency").HasMaxLength(3); builder.Property(x => x.FunctionalCurrency).HasColumnName("functional_currency").HasMaxLength(3); builder.Property(x => x.EffectiveRate).HasColumnName("effective_rate").HasPrecision(38, 18); builder.Property(x => x.RateDate).HasColumnName("rate_date");
        builder.Property(x => x.RateSetIdentity).HasColumnName("rate_set_identity").HasMaxLength(1000); builder.Property(x => x.ObservationIdentity).HasColumnName("observation_identity").HasMaxLength(1000); builder.Property(x => x.EvidenceChecksum).HasColumnName("evidence_checksum").HasMaxLength(64);
        builder.HasIndex(x => new { x.CompanyId, x.RunId, x.PopulationItemId }).IsUnique(); builder.HasIndex(x => new { x.CompanyId, x.ExchangeRateConversionId });
        builder.HasOne(x => x.Run).WithMany(x => x.RateBindings).HasForeignKey(x => new { x.CompanyId, x.RunId }).HasPrincipalKey(x => new { x.CompanyId, x.Id }).OnDelete(DeleteBehavior.Cascade);
        builder.HasOne(x => x.PopulationItem).WithMany().HasForeignKey(x => new { x.CompanyId, x.PopulationItemId }).HasPrincipalKey(x => new { x.CompanyId, x.Id }).OnDelete(DeleteBehavior.NoAction);
        builder.HasOne(x => x.ExchangeRateConversion).WithMany().HasForeignKey(x => new { x.CompanyId, x.ExchangeRateConversionId }).HasPrincipalKey(x => new { x.CompanyId, x.Id }).OnDelete(DeleteBehavior.Restrict);
    }
}

public sealed class CurrencyRevaluationProposalLineConfiguration : IEntityTypeConfiguration<CurrencyRevaluationProposalLine>
{
    public void Configure(EntityTypeBuilder<CurrencyRevaluationProposalLine> builder)
    {
        builder.ToTable("currency_revaluation_proposal_lines"); builder.HasKey(x => x.Id);
        builder.Property(x => x.Id).HasColumnName("id"); builder.Property(x => x.CompanyId).HasColumnName("company_id"); builder.Property(x => x.RunId).HasColumnName("run_id"); builder.Property(x => x.Sequence).HasColumnName("sequence"); builder.Property(x => x.FinanceAccountId).HasColumnName("finance_account_id"); builder.Property(x => x.PopulationItemId).HasColumnName("population_item_id"); builder.Property(x => x.LineType).HasColumnName("line_type").HasMaxLength(32);
        builder.Property(x => x.DebitAmount).HasColumnName("debit_amount").HasPrecision(38, 18); builder.Property(x => x.CreditAmount).HasColumnName("credit_amount").HasPrecision(38, 18); builder.Property(x => x.Currency).HasColumnName("currency").HasMaxLength(3); builder.Property(x => x.Description).HasColumnName("description").HasMaxLength(500);
        builder.HasIndex(x => new { x.CompanyId, x.RunId, x.Sequence }).IsUnique();
        builder.HasOne(x => x.Run).WithMany(x => x.ProposalLines).HasForeignKey(x => new { x.CompanyId, x.RunId }).HasPrincipalKey(x => new { x.CompanyId, x.Id }).OnDelete(DeleteBehavior.Cascade);
        builder.HasOne(x => x.FinanceAccount).WithMany().HasForeignKey(x => new { x.CompanyId, x.FinanceAccountId }).HasPrincipalKey(x => new { x.CompanyId, x.Id }).OnDelete(DeleteBehavior.Restrict);
        builder.HasOne(x => x.PopulationItem).WithMany().HasForeignKey(x => new { x.CompanyId, x.PopulationItemId }).HasPrincipalKey(x => new { x.CompanyId, x.Id }).OnDelete(DeleteBehavior.NoAction);
    }
}

public sealed class CurrencyRevaluationReviewConfiguration : IEntityTypeConfiguration<CurrencyRevaluationReview>
{
    public void Configure(EntityTypeBuilder<CurrencyRevaluationReview> builder)
    {
        builder.ToTable("currency_revaluation_reviews"); builder.HasKey(x => x.Id); builder.Property(x => x.Id).HasColumnName("id"); builder.Property(x => x.CompanyId).HasColumnName("company_id"); builder.Property(x => x.RunId).HasColumnName("run_id"); builder.Property(x => x.PopulationItemId).HasColumnName("population_item_id"); builder.Property(x => x.Action).HasColumnName("action").HasMaxLength(32); builder.Property(x => x.Reason).HasColumnName("reason").HasMaxLength(1000); builder.Property(x => x.ActorUserId).HasColumnName("actor_user_id"); builder.Property(x => x.ApprovalRequestId).HasColumnName("approval_request_id"); builder.Property(x => x.EvidenceChecksum).HasColumnName("evidence_checksum").HasMaxLength(64); builder.Property(x => x.OccurredUtc).HasColumnName("occurred_utc");
        builder.HasIndex(x => new { x.CompanyId, x.RunId, x.OccurredUtc });
        builder.HasOne(x => x.Run).WithMany(x => x.Reviews).HasForeignKey(x => new { x.CompanyId, x.RunId }).HasPrincipalKey(x => new { x.CompanyId, x.Id }).OnDelete(DeleteBehavior.Cascade);
        builder.HasOne(x => x.PopulationItem).WithMany().HasForeignKey(x => new { x.CompanyId, x.PopulationItemId }).HasPrincipalKey(x => new { x.CompanyId, x.Id }).OnDelete(DeleteBehavior.NoAction);
    }
}

public sealed class CurrencyRevaluationReconciliationConfiguration : IEntityTypeConfiguration<CurrencyRevaluationReconciliation>
{
    public void Configure(EntityTypeBuilder<CurrencyRevaluationReconciliation> builder)
    {
        builder.ToTable("currency_revaluation_reconciliations"); builder.HasKey(x => x.Id); builder.Property(x => x.Id).HasColumnName("id"); builder.Property(x => x.CompanyId).HasColumnName("company_id"); builder.Property(x => x.RunId).HasColumnName("run_id"); builder.Property(x => x.ReconciliationType).HasColumnName("reconciliation_type").HasMaxLength(32); builder.Property(x => x.PopulationCount).HasColumnName("population_count"); builder.Property(x => x.CarryingAmount).HasColumnName("carrying_amount").HasPrecision(38, 18); builder.Property(x => x.RevaluedAmount).HasColumnName("revalued_amount").HasPrecision(38, 18); builder.Property(x => x.ProposedAdjustment).HasColumnName("proposed_adjustment").HasPrecision(38, 18); builder.Property(x => x.ProposalLineAdjustment).HasColumnName("proposal_line_adjustment").HasPrecision(38, 18); builder.Property(x => x.Difference).HasColumnName("difference").HasPrecision(38, 18); builder.Property(x => x.Currency).HasColumnName("currency").HasMaxLength(3); builder.Property(x => x.Checksum).HasColumnName("checksum").HasMaxLength(64); builder.Property(x => x.IsReconciled).HasColumnName("is_reconciled");
        builder.HasIndex(x => new { x.CompanyId, x.RunId, x.ReconciliationType }).IsUnique(); builder.HasOne(x => x.Run).WithMany(x => x.Reconciliations).HasForeignKey(x => new { x.CompanyId, x.RunId }).HasPrincipalKey(x => new { x.CompanyId, x.Id }).OnDelete(DeleteBehavior.Cascade);
    }
}

public sealed class CurrencyRevaluationAccountPolicyConfiguration : IEntityTypeConfiguration<CurrencyRevaluationAccountPolicy>
{
    public void Configure(EntityTypeBuilder<CurrencyRevaluationAccountPolicy> builder)
    {
        builder.ToTable("currency_revaluation_account_policies", table => table.HasCheckConstraint("CK_currency_revaluation_account_class", "monetary_class IN ('cash','receivable','payable','other')")); builder.HasKey(x => x.Id);
        builder.Property(x => x.Id).HasColumnName("id"); builder.Property(x => x.CompanyId).HasColumnName("company_id"); builder.Property(x => x.FinanceAccountId).HasColumnName("finance_account_id"); builder.Property(x => x.MonetaryClass).HasColumnName("monetary_class").HasMaxLength(32); builder.Property(x => x.IsEnabled).HasColumnName("is_enabled"); builder.Property(x => x.UpdatedByUserId).HasColumnName("updated_by_user_id"); builder.Property(x => x.UpdatedUtc).HasColumnName("updated_utc"); builder.Property(x => x.Version).HasColumnName("version").IsConcurrencyToken();
        builder.HasIndex(x => new { x.CompanyId, x.FinanceAccountId }).IsUnique(); builder.HasOne(x => x.FinanceAccount).WithMany().HasForeignKey(x => new { x.CompanyId, x.FinanceAccountId }).HasPrincipalKey(x => new { x.CompanyId, x.Id }).OnDelete(DeleteBehavior.Restrict);
    }
}

public sealed class CurrencyRevaluationScheduleConfiguration : IEntityTypeConfiguration<CurrencyRevaluationSchedule>
{
    public void Configure(EntityTypeBuilder<CurrencyRevaluationSchedule> builder)
    {
        builder.ToTable("currency_revaluation_schedules", table => table.HasCheckConstraint("CK_currency_revaluation_schedule_days", "days_before_period_end >= 0 AND days_before_period_end <= 31")); builder.HasKey(x => x.Id);
        builder.Property(x => x.Id).HasColumnName("id"); builder.Property(x => x.CompanyId).HasColumnName("company_id"); builder.Property(x => x.IsEnabled).HasColumnName("is_enabled"); builder.Property(x => x.DaysBeforePeriodEnd).HasColumnName("days_before_period_end"); builder.Property(x => x.AutomaticReversal).HasColumnName("automatic_reversal"); builder.Property(x => x.VoucherSeriesCode).HasColumnName("voucher_series_code").HasMaxLength(32); builder.Property(x => x.UpdatedByUserId).HasColumnName("updated_by_user_id"); builder.Property(x => x.UpdatedUtc).HasColumnName("updated_utc"); builder.Property(x => x.LastEvaluatedUtc).HasColumnName("last_evaluated_utc"); builder.Property(x => x.Version).HasColumnName("version").IsConcurrencyToken();
        builder.HasIndex(x => x.CompanyId).IsUnique(); builder.HasOne<Company>().WithMany().HasForeignKey(x => x.CompanyId).OnDelete(DeleteBehavior.Cascade);
    }
}
