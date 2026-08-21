using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using VirtualCompany.Domain.Entities;

namespace VirtualCompany.Infrastructure.Persistence.Configurations;

public sealed class ManualJournalDraftConfiguration : IEntityTypeConfiguration<ManualJournalDraft>
{
    public void Configure(EntityTypeBuilder<ManualJournalDraft> builder)
    {
        builder.ToTable("manual_journal_drafts");
        builder.HasKey(x => x.Id);
        builder.HasAlternateKey(x => new { x.CompanyId, x.Id });
        builder.Property(x => x.Id).HasColumnName("id");
        builder.Property(x => x.CompanyId).HasColumnName("company_id").IsRequired();
        builder.Property(x => x.FiscalPeriodId).HasColumnName("fiscal_period_id").IsRequired();
        builder.Property(x => x.VoucherSeriesCode).HasColumnName("voucher_series_code").HasMaxLength(32).IsRequired();
        builder.Property(x => x.DocumentDate).HasColumnName("document_date").HasColumnType("date").IsRequired();
        builder.Property(x => x.PostingDate).HasColumnName("posting_date").HasColumnType("date").IsRequired();
        builder.Property(x => x.Explanation).HasColumnName("explanation").HasMaxLength(1000).IsRequired();
        builder.Property(x => x.Currency).HasColumnName("currency").HasMaxLength(3).IsRequired();
        builder.Property(x => x.Status).HasColumnName("status").HasMaxLength(32).IsRequired();
        builder.Property(x => x.Version).HasColumnName("version").IsConcurrencyToken().IsRequired();
        builder.Property(x => x.PayloadHash).HasColumnName("payload_hash").HasMaxLength(64).IsRequired();
        builder.Property(x => x.CreatedByUserId).HasColumnName("created_by_user_id").IsRequired();
        builder.Property(x => x.UpdatedByUserId).HasColumnName("updated_by_user_id").IsRequired();
        builder.Property(x => x.ApprovalRequestId).HasColumnName("approval_request_id");
        builder.Property(x => x.LedgerEntryId).HasColumnName("ledger_entry_id");
        builder.Property(x => x.OriginalLedgerEntryId).HasColumnName("original_ledger_entry_id");
        builder.Property(x => x.CorrectionReason).HasColumnName("correction_reason").HasMaxLength(1000);
        builder.Property(x => x.CreatedUtc).HasColumnName("created_utc").IsRequired();
        builder.Property(x => x.UpdatedUtc).HasColumnName("updated_utc").IsRequired();
        builder.Property(x => x.PostedUtc).HasColumnName("posted_utc");
        builder.Property(x => x.DiscardedUtc).HasColumnName("discarded_utc");
        builder.HasIndex(x => new { x.CompanyId, x.Status, x.UpdatedUtc });
        builder.HasIndex(x => new { x.CompanyId, x.ApprovalRequestId }).IsUnique().HasFilter("approval_request_id IS NOT NULL");
        builder.HasIndex(x => new { x.CompanyId, x.LedgerEntryId }).IsUnique().HasFilter("ledger_entry_id IS NOT NULL");
        builder.HasOne(x => x.Company).WithMany().HasForeignKey(x => x.CompanyId).OnDelete(DeleteBehavior.Cascade);
        builder.HasOne(x => x.FiscalPeriod).WithMany()
            .HasForeignKey(x => new { x.CompanyId, x.FiscalPeriodId }).HasPrincipalKey(x => new { x.CompanyId, x.Id }).OnDelete(DeleteBehavior.Restrict);
        builder.HasOne(x => x.ApprovalRequest).WithMany()
            .HasForeignKey(x => new { x.CompanyId, x.ApprovalRequestId }).HasPrincipalKey(x => new { x.CompanyId, x.Id }).OnDelete(DeleteBehavior.Restrict);
        builder.HasOne(x => x.LedgerEntry).WithMany()
            .HasForeignKey(x => new { x.CompanyId, x.LedgerEntryId }).HasPrincipalKey(x => new { x.CompanyId, x.Id }).OnDelete(DeleteBehavior.Restrict);
        builder.HasOne(x => x.OriginalLedgerEntry).WithMany()
            .HasForeignKey(x => new { x.CompanyId, x.OriginalLedgerEntryId }).HasPrincipalKey(x => new { x.CompanyId, x.Id }).OnDelete(DeleteBehavior.Restrict);
    }
}
