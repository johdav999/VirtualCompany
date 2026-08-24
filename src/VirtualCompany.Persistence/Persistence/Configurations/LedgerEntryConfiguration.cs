using System.Text.Json;
using System.Text.Json.Nodes;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using Microsoft.EntityFrameworkCore.ChangeTracking;
using Microsoft.EntityFrameworkCore.Storage.ValueConversion;
using VirtualCompany.Domain.Entities;
using VirtualCompany.Domain.Enums;

namespace VirtualCompany.Infrastructure.Persistence;
internal sealed class LedgerEntryConfiguration : IEntityTypeConfiguration<LedgerEntry>
{
    public void Configure(EntityTypeBuilder<LedgerEntry> builder)
    {
        builder.ToTable("ledger_entries");

        builder.HasKey(x => x.Id);
        builder.HasAlternateKey(x => new { x.CompanyId, x.Id });
        builder.Property(x => x.Id).HasColumnName("id");
        builder.Property(x => x.CompanyId).HasColumnName("company_id").IsRequired();
        builder.Property(x => x.FiscalPeriodId).HasColumnName("fiscal_period_id").IsRequired();
        builder.Property(x => x.EntryNumber).HasColumnName("entry_number").HasMaxLength(64).IsRequired();
        builder.Property(x => x.EntryUtc).HasColumnName("entry_at").IsRequired();
        builder.Property(x => x.Status).HasColumnName("status").HasMaxLength(32).IsRequired();
        builder.Property(x => x.SourceType).HasColumnName("source_type").HasMaxLength(64);
        builder.Property(x => x.SourceId).HasColumnName("source_id").HasMaxLength(128);
        builder.Property(x => x.PostedAtUtc).HasColumnName("posted_at");
        builder.Property(x => x.Description).HasColumnName("description").HasMaxLength(500);
        builder.Property(x => x.CreatedUtc).HasColumnName("created_at").IsRequired();
        builder.Property(x => x.UpdatedUtc).HasColumnName("updated_at").IsRequired();
        builder.Property(x => x.VoucherSeriesId).HasColumnName("voucher_series_id");
        builder.Property(x => x.VoucherSequenceNumber).HasColumnName("voucher_sequence_number");
        builder.Property(x => x.VoucherFiscalYear).HasColumnName("voucher_fiscal_year");
        builder.Property(x => x.DocumentDate).HasColumnName("document_date").HasColumnType("date");
        builder.Property(x => x.PostingDate).HasColumnName("posting_date").HasColumnType("date");
        builder.Property(x => x.BaseCurrency).HasColumnName("base_currency").HasMaxLength(3);
        builder.Property(x => x.PostingType).HasColumnName("posting_type").HasMaxLength(32);
        builder.Property(x => x.SourceVersion).HasColumnName("source_version").HasMaxLength(128);
        builder.Property(x => x.IdempotencyKey).HasColumnName("idempotency_key").HasMaxLength(200);
        builder.Property(x => x.PolicyPackKey).HasColumnName("policy_pack_key").HasMaxLength(96);
        builder.Property(x => x.PolicyPackVersion).HasColumnName("policy_pack_version").HasMaxLength(32);
        builder.Property(x => x.PolicyFactsJson).HasColumnName("policy_facts_json").HasMaxLength(16000);
        builder.Property(x => x.PostedByUserId).HasColumnName("posted_by_user_id");
        builder.Property(x => x.ApprovalRequestId).HasColumnName("approval_request_id");
        builder.Property(x => x.OriginalLedgerEntryId).HasColumnName("original_ledger_entry_id");
        builder.Property(x => x.CorrectionReason).HasColumnName("correction_reason").HasMaxLength(1000);
        builder.Property(x => x.RowVersion).HasColumnName("row_version").IsRowVersion().IsConcurrencyToken();

        builder.HasIndex(x => new { x.CompanyId, x.FiscalPeriodId, x.EntryUtc });
        builder.HasIndex(x => new { x.CompanyId, x.FiscalPeriodId, x.Status, x.EntryUtc, x.EntryNumber });
        builder.HasIndex(x => new { x.CompanyId, x.SourceType, x.SourceId, x.PostedAtUtc }).IsUnique().HasFilter("source_type IS NOT NULL AND source_id IS NOT NULL AND posted_at IS NOT NULL");
        builder.HasIndex(x => new { x.CompanyId, x.EntryNumber }).IsUnique();
        builder.HasIndex(x => new { x.CompanyId, x.Status, x.EntryUtc });
        builder.HasIndex(x => new { x.CompanyId, x.VoucherSeriesId, x.VoucherFiscalYear, x.VoucherSequenceNumber }).IsUnique()
            .HasFilter("voucher_series_id IS NOT NULL AND voucher_fiscal_year IS NOT NULL AND voucher_sequence_number IS NOT NULL");
        builder.HasIndex(x => new { x.CompanyId, x.IdempotencyKey }).IsUnique().HasFilter("idempotency_key IS NOT NULL");
        builder.HasIndex(x => new { x.CompanyId, x.SourceType, x.SourceId, x.SourceVersion, x.PostingType });
        builder.HasOne(x => x.Company).WithMany().HasForeignKey(x => x.CompanyId).OnDelete(DeleteBehavior.Cascade);
        builder.HasOne(x => x.FiscalPeriod)
            .WithMany()
            .HasForeignKey(x => new { x.CompanyId, x.FiscalPeriodId })
            .HasPrincipalKey(x => new { x.CompanyId, x.Id })
            .OnDelete(DeleteBehavior.Restrict);
        builder.HasOne(x => x.VoucherSeries)
            .WithMany()
            .HasForeignKey(x => new { x.CompanyId, x.VoucherSeriesId })
            .HasPrincipalKey(x => new { x.CompanyId, x.Id })
            .OnDelete(DeleteBehavior.Restrict);
        builder.HasOne(x => x.OriginalLedgerEntry)
            .WithMany(x => x.Corrections)
            .HasForeignKey(x => new { x.CompanyId, x.OriginalLedgerEntryId })
            .HasPrincipalKey(x => new { x.CompanyId, x.Id })
            .OnDelete(DeleteBehavior.NoAction);
        builder.HasOne(x => x.ApprovalRequest)
            .WithMany()
            .HasForeignKey(x => new { x.CompanyId, x.ApprovalRequestId })
            .HasPrincipalKey(x => new { x.CompanyId, x.Id })
            .OnDelete(DeleteBehavior.Restrict);
    }
}

