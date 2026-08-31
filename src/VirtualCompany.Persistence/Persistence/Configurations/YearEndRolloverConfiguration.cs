using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using VirtualCompany.Domain.Entities;

namespace VirtualCompany.Infrastructure.Persistence.Configurations;

internal sealed class YearEndRunConfiguration : IEntityTypeConfiguration<YearEndRun>
{
    public void Configure(EntityTypeBuilder<YearEndRun> b)
    {
        b.ToTable("year_end_runs"); b.HasKey(x => x.Id);
        b.Property(x => x.VoucherSeriesCode).HasMaxLength(32); b.Property(x => x.Status).HasMaxLength(32);
        b.Property(x => x.ApprovedEvidenceHash).HasMaxLength(64); b.Property(x => x.OpeningBalanceChecksum).HasMaxLength(64);
        b.Property(x => x.FailureCode).HasMaxLength(100); b.Property(x => x.FailureSummary).HasMaxLength(1000);
        b.Property(x => x.Version).IsConcurrencyToken();
        b.HasIndex(x => new { x.CompanyId, x.FiscalYearStart }).IsUnique();
        b.HasIndex(x => new { x.CompanyId, x.Status, x.UpdatedUtc });
        b.HasOne(x => x.TargetFiscalPeriod).WithMany().HasForeignKey(x => x.TargetFiscalPeriodId).OnDelete(DeleteBehavior.Restrict);
        b.HasOne<FinanceAccount>().WithMany().HasForeignKey(x => x.RetainedEarningsAccountId).OnDelete(DeleteBehavior.Restrict);
        b.HasOne<FinanceAccount>().WithMany().HasForeignKey(x => x.OpeningBalanceClearingAccountId).OnDelete(DeleteBehavior.Restrict);
        b.HasOne<LedgerEntry>().WithMany().HasForeignKey(x => x.RetainedEarningsLedgerEntryId).OnDelete(DeleteBehavior.Restrict);
        b.HasOne<LedgerEntry>().WithMany().HasForeignKey(x => x.OpeningBalanceLedgerEntryId).OnDelete(DeleteBehavior.Restrict);
    }
}

internal sealed class YearEndReadinessSnapshotConfiguration : IEntityTypeConfiguration<YearEndReadinessSnapshot>
{
    public void Configure(EntityTypeBuilder<YearEndReadinessSnapshot> b)
    {
        b.ToTable("year_end_readiness_snapshots"); b.HasKey(x => x.Id); b.Property(x => x.Status).HasMaxLength(32);
        b.Property(x => x.EvidenceHash).HasMaxLength(64); b.Property(x => x.JournalCutoffHash).HasMaxLength(64);
        b.Property(x => x.EvidenceJson).HasColumnType("nvarchar(max)"); b.Property(x => x.Version).IsConcurrencyToken();
        b.HasIndex(x => new { x.CompanyId, x.RunId, x.SnapshotNumber }).IsUnique();
        b.HasOne(x => x.Run).WithMany(x => x.ReadinessSnapshots).HasForeignKey(x => x.RunId).OnDelete(DeleteBehavior.Cascade);
    }
}

internal sealed class YearEndRetainedEarningsProposalConfiguration : IEntityTypeConfiguration<YearEndRetainedEarningsProposal>
{
    public void Configure(EntityTypeBuilder<YearEndRetainedEarningsProposal> b)
    {
        b.ToTable("year_end_retained_earnings_proposals"); b.HasKey(x => x.Id);
        b.Property(x => x.NetIncome).HasPrecision(19, 4); b.Property(x => x.Currency).HasMaxLength(3);
        b.Property(x => x.EvidenceHash).HasMaxLength(64); b.Property(x => x.Status).HasMaxLength(32); b.Property(x => x.Version).IsConcurrencyToken();
        b.HasIndex(x => new { x.CompanyId, x.RunId, x.EvidenceHash });
        b.HasOne(x => x.Run).WithMany(x => x.RetainedEarningsProposals).HasForeignKey(x => x.RunId).OnDelete(DeleteBehavior.Cascade);
        b.HasOne<FinanceAccount>().WithMany().HasForeignKey(x => x.RetainedEarningsAccountId).OnDelete(DeleteBehavior.Restrict);
        b.HasOne<FinanceAccount>().WithMany().HasForeignKey(x => x.OpeningBalanceClearingAccountId).OnDelete(DeleteBehavior.Restrict);
    }
}

internal sealed class YearEndOpeningBalanceCandidateConfiguration : IEntityTypeConfiguration<YearEndOpeningBalanceCandidate>
{
    public void Configure(EntityTypeBuilder<YearEndOpeningBalanceCandidate> b)
    {
        b.ToTable("year_end_opening_balance_candidates"); b.HasKey(x => x.Id);
        b.Property(x => x.AccountCode).HasMaxLength(32); b.Property(x => x.AccountName).HasMaxLength(160); b.Property(x => x.AccountClass).HasMaxLength(32);
        b.Property(x => x.SourceCurrency).HasMaxLength(3); b.Property(x => x.DimensionKey).HasMaxLength(1000); b.Property(x => x.DimensionFactsJson).HasColumnType("nvarchar(max)");
        b.Property(x => x.ClosingFunctionalBalance).HasPrecision(19, 4); b.Property(x => x.ClosingDocumentBalance).HasPrecision(19, 4);
        b.Property(x => x.OpeningFunctionalBalance).HasPrecision(19, 4); b.Property(x => x.OpeningDocumentBalance).HasPrecision(19, 4);
        b.Property(x => x.Difference).HasPrecision(19, 4); b.Property(x => x.Status).HasMaxLength(32);
        b.HasIndex(x => new { x.CompanyId, x.RunId, x.FinanceAccountId });
        b.HasOne(x => x.Run).WithMany(x => x.OpeningBalanceCandidates).HasForeignKey(x => x.RunId).OnDelete(DeleteBehavior.Cascade);
        b.HasOne(x => x.FinanceAccount).WithMany().HasForeignKey(x => x.FinanceAccountId).OnDelete(DeleteBehavior.Restrict);
        b.HasOne<LedgerEntry>().WithMany().HasForeignKey(x => x.OpeningLedgerEntryId).OnDelete(DeleteBehavior.Restrict);
    }
}

internal sealed class YearEndApprovalSignOffConfiguration : IEntityTypeConfiguration<YearEndApprovalSignOff>
{
    public void Configure(EntityTypeBuilder<YearEndApprovalSignOff> b)
    {
        b.ToTable("year_end_approval_signoffs"); b.HasKey(x => x.Id); b.Property(x => x.Action).HasMaxLength(64);
        b.Property(x => x.Decision).HasMaxLength(32); b.Property(x => x.EvidenceHash).HasMaxLength(64); b.Property(x => x.ActorRole).HasMaxLength(64);
        b.Property(x => x.Reason).HasMaxLength(2000); b.HasIndex(x => new { x.CompanyId, x.RunId, x.OccurredUtc });
        b.HasOne(x => x.Run).WithMany(x => x.SignOffs).HasForeignKey(x => x.RunId).OnDelete(DeleteBehavior.Cascade);
    }
}

internal sealed class YearEndSubsequentEventConfiguration : IEntityTypeConfiguration<YearEndSubsequentEvent>
{
    public void Configure(EntityTypeBuilder<YearEndSubsequentEvent> b)
    {
        b.ToTable("year_end_subsequent_events"); b.HasKey(x => x.Id); b.Property(x => x.Title).HasMaxLength(240);
        b.Property(x => x.Description).HasMaxLength(4000); b.Property(x => x.EstimatedAmount).HasPrecision(19, 4);
        b.Property(x => x.Currency).HasMaxLength(3); b.Property(x => x.Decision).HasMaxLength(32); b.Property(x => x.Status).HasMaxLength(32);
        b.Property(x => x.Version).IsConcurrencyToken(); b.HasIndex(x => new { x.CompanyId, x.RunId, x.EventDate });
        b.HasOne(x => x.Run).WithMany(x => x.SubsequentEvents).HasForeignKey(x => x.RunId).OnDelete(DeleteBehavior.Cascade);
        b.HasOne<CompanyKnowledgeDocument>().WithMany().HasForeignKey(x => x.EvidenceDocumentId).OnDelete(DeleteBehavior.Restrict);
        b.HasOne<LedgerEntry>().WithMany().HasForeignKey(x => x.CorrectionLedgerEntryId).OnDelete(DeleteBehavior.Restrict);
        b.HasOne<AccountingCloseReopenRequest>().WithMany().HasForeignKey(x => x.ReopenRequestId).OnDelete(DeleteBehavior.Restrict);
    }
}

internal sealed class YearEndHistoryConfiguration : IEntityTypeConfiguration<YearEndHistory>
{
    public void Configure(EntityTypeBuilder<YearEndHistory> b)
    {
        b.ToTable("year_end_history"); b.HasKey(x => x.Id); b.Property(x => x.Action).HasMaxLength(100);
        b.Property(x => x.FromStatus).HasMaxLength(32); b.Property(x => x.ToStatus).HasMaxLength(32); b.Property(x => x.EvidenceHash).HasMaxLength(64);
        b.Property(x => x.Summary).HasMaxLength(2000); b.HasIndex(x => new { x.CompanyId, x.RunId, x.OccurredUtc });
        b.HasOne(x => x.Run).WithMany(x => x.History).HasForeignKey(x => x.RunId).OnDelete(DeleteBehavior.Cascade);
    }
}

internal sealed class YearEndCorrectionRecordConfiguration : IEntityTypeConfiguration<YearEndCorrectionRecord>
{
    public void Configure(EntityTypeBuilder<YearEndCorrectionRecord> b)
    {
        b.ToTable("year_end_correction_records"); b.HasKey(x => x.Id); b.Property(x => x.CorrectionMode).HasMaxLength(32); b.Property(x => x.Reason).HasMaxLength(2000);
        b.HasIndex(x => new { x.CompanyId, x.SubsequentEventId }).IsUnique();
        b.HasOne(x => x.Run).WithMany().HasForeignKey(x => x.RunId).OnDelete(DeleteBehavior.Restrict);
        b.HasOne(x => x.SubsequentEvent).WithMany().HasForeignKey(x => x.SubsequentEventId).OnDelete(DeleteBehavior.Restrict);
        b.HasOne<LedgerEntry>().WithMany().HasForeignKey(x => x.LedgerEntryId).OnDelete(DeleteBehavior.Restrict);
        b.HasOne<AccountingCloseReopenRequest>().WithMany().HasForeignKey(x => x.ReopenRequestId).OnDelete(DeleteBehavior.Restrict);
    }
}

internal sealed class YearEndOperationConfiguration : IEntityTypeConfiguration<YearEndOperation>
{
    public void Configure(EntityTypeBuilder<YearEndOperation> b)
    {
        b.ToTable("year_end_operations"); b.HasKey(x => x.Id); b.Property(x => x.Operation).HasMaxLength(64);
        b.Property(x => x.IdempotencyKey).HasMaxLength(200); b.Property(x => x.RequestHash).HasMaxLength(64);
        b.HasIndex(x => new { x.CompanyId, x.IdempotencyKey }).IsUnique();
        b.HasOne(x => x.Run).WithMany().HasForeignKey(x => x.RunId).OnDelete(DeleteBehavior.Cascade);
    }
}
