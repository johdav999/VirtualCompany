using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using VirtualCompany.Domain.Entities;
using VirtualCompany.Domain.Enums;

namespace VirtualCompany.Infrastructure.Persistence;

internal sealed class PaymentCashLedgerLinkEntityConfiguration : IEntityTypeConfiguration<PaymentCashLedgerLink>
{
    public void Configure(EntityTypeBuilder<PaymentCashLedgerLink> builder)
    {
        builder.ToTable("payment_cash_ledger_links");

        builder.HasKey(x => x.Id);
        builder.Property(x => x.Id).HasColumnName("id");
        builder.Property(x => x.CompanyId).HasColumnName("company_id").IsRequired();
        builder.Property(x => x.PaymentId).HasColumnName("payment_id").IsRequired();
        builder.Property(x => x.LedgerEntryId).HasColumnName("ledger_entry_id").IsRequired();
        builder.Property(x => x.SourceType).HasColumnName("source_type").HasMaxLength(64).IsRequired();
        builder.Property(x => x.SourceId).HasColumnName("source_id").HasMaxLength(128).IsRequired();
        builder.Property(x => x.PostedAtUtc).HasColumnName("posted_at").IsRequired();
        builder.Property(x => x.CreatedUtc).HasColumnName("created_at").IsRequired();

        builder.HasIndex(x => new { x.CompanyId, x.PaymentId, x.LedgerEntryId }).IsUnique();
        builder.HasIndex(x => new { x.CompanyId, x.PaymentId, x.SourceType, x.SourceId, x.PostedAtUtc }).IsUnique();
        builder.HasOne(x => x.Company).WithMany().HasForeignKey(x => x.CompanyId).OnDelete(DeleteBehavior.Cascade);
        builder.HasOne(x => x.Payment).WithMany(x => x.CashLedgerLinks).HasForeignKey(x => x.PaymentId).OnDelete(DeleteBehavior.Cascade);
        builder.HasOne(x => x.LedgerEntry).WithMany().HasForeignKey(x => x.LedgerEntryId).OnDelete(DeleteBehavior.Cascade);
    }
}

internal sealed class BankTransactionPostingStateRecordEntityConfiguration : IEntityTypeConfiguration<BankTransactionPostingStateRecord>
{
    public void Configure(EntityTypeBuilder<BankTransactionPostingStateRecord> builder)
    {
        builder.ToTable("bank_transaction_posting_states");

        builder.HasKey(x => x.Id);
        builder.Property(x => x.Id).HasColumnName("id");
        builder.Property(x => x.CompanyId).HasColumnName("company_id").IsRequired();
        builder.Property(x => x.BankTransactionId).HasColumnName("bank_transaction_id").IsRequired();
        builder.Property(x => x.MatchingStatus).HasColumnName("matching_status").HasMaxLength(32).IsRequired();
        builder.Property(x => x.PostingState).HasColumnName("posting_state").HasMaxLength(32).IsRequired();
        builder.Property(x => x.LinkedPaymentCount).HasColumnName("linked_payment_count").IsRequired();
        builder.Property(x => x.LastEvaluatedUtc).HasColumnName("last_evaluated_at").IsRequired();
        builder.Property(x => x.UnmatchedReason).HasColumnName("unmatched_reason").HasMaxLength(128);
        builder.Property(x => x.ConflictCode).HasColumnName("conflict_code").HasMaxLength(64);
        builder.Property(x => x.ConflictDetails).HasColumnName("conflict_details").HasMaxLength(512);
        builder.Property(x => x.CreatedUtc).HasColumnName("created_at").IsRequired();
        builder.Property(x => x.UpdatedUtc).HasColumnName("updated_at").IsRequired();
        builder.HasCheckConstraint("CK_bank_transaction_posting_states_matching_status", BankTransactionMatchingStatuses.BuildCheckConstraintSql("matching_status"));
        builder.HasCheckConstraint("CK_bank_transaction_posting_states_posting_state", BankTransactionPostingStates.BuildCheckConstraintSql("posting_state"));
        builder.HasCheckConstraint("CK_bank_transaction_posting_states_linked_payment_count", "linked_payment_count >= 0");
        builder.HasIndex(x => new { x.CompanyId, x.BankTransactionId }).IsUnique();
        builder.HasIndex(x => new { x.CompanyId, x.MatchingStatus, x.PostingState });
        builder.HasOne(x => x.Company).WithMany().HasForeignKey(x => x.CompanyId).OnDelete(DeleteBehavior.Cascade);
        builder.HasOne(x => x.BankTransaction).WithOne(x => x.PostingStateRecord).HasForeignKey<BankTransactionPostingStateRecord>(x => x.BankTransactionId).OnDelete(DeleteBehavior.Cascade);
    }
}