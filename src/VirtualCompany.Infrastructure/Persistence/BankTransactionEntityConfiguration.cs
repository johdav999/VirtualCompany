using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using VirtualCompany.Domain.Entities;
using VirtualCompany.Domain.Enums;

namespace VirtualCompany.Infrastructure.Persistence;

internal sealed class CompanyBankAccountEntityConfiguration : IEntityTypeConfiguration<CompanyBankAccount>
{
    public void Configure(EntityTypeBuilder<CompanyBankAccount> builder)
    {
        builder.ToTable("company_bank_accounts");

        builder.HasKey(x => x.Id);
        builder.Property(x => x.Id).HasColumnName("id");
        builder.Property(x => x.CompanyId).HasColumnName("company_id").IsRequired();
        builder.Property(x => x.FinanceAccountId).HasColumnName("finance_account_id").IsRequired();
        builder.Property(x => x.DisplayName).HasColumnName("display_name").HasMaxLength(120).IsRequired();
        builder.Property(x => x.BankName).HasColumnName("bank_name").HasMaxLength(120).IsRequired();
        builder.Property(x => x.MaskedAccountNumber).HasColumnName("masked_account_number").HasMaxLength(64).IsRequired();
        builder.Property(x => x.Currency).HasColumnName("currency").HasMaxLength(3).IsRequired();
        builder.Property(x => x.ExternalCode).HasColumnName("external_code").HasMaxLength(64);
        builder.Property(x => x.IsPrimary).HasColumnName("is_primary").HasDefaultValue(false).IsRequired();
        builder.Property(x => x.IsActive).HasColumnName("is_active").HasDefaultValue(true).IsRequired();
        builder.Property(x => x.CreatedUtc).HasColumnName("created_at").IsRequired();
        builder.Property(x => x.UpdatedUtc).HasColumnName("updated_at").IsRequired();

        builder.HasIndex(x => x.CompanyId);
        builder.HasIndex(x => new { x.CompanyId, x.DisplayName });
        builder.HasIndex(x => new { x.CompanyId, x.ExternalCode }).IsUnique().HasFilter("\"external_code\" IS NOT NULL");

        builder.HasOne(x => x.Company).WithMany().HasForeignKey(x => x.CompanyId).OnDelete(DeleteBehavior.Cascade);
        builder.HasOne(x => x.FinanceAccount).WithMany().HasForeignKey(x => x.FinanceAccountId).OnDelete(DeleteBehavior.Restrict);
    }
}

internal sealed class BankTransactionEntityConfiguration : IEntityTypeConfiguration<BankTransaction>
{
    public void Configure(EntityTypeBuilder<BankTransaction> builder)
    {
        builder.ToTable("bank_transactions");

        builder.HasKey(x => x.Id);
        builder.Property(x => x.Id).HasColumnName("id");
        builder.Property(x => x.CompanyId).HasColumnName("company_id").IsRequired();
        builder.Property(x => x.BankAccountId).HasColumnName("bank_account_id").IsRequired();
        builder.Property(x => x.BookingDate).HasColumnName("booking_date").IsRequired();
        builder.Property(x => x.ValueDate).HasColumnName("value_date").IsRequired();
        builder.Property(x => x.Amount).HasColumnName("amount").HasColumnType("decimal(18,2)").IsRequired();
        builder.Property(x => x.Currency).HasColumnName("currency").HasMaxLength(3).IsRequired();
        builder.Property(x => x.ReferenceText).HasColumnName("reference_text").HasMaxLength(240).IsRequired();
        builder.Property(x => x.Counterparty).HasColumnName("counterparty").HasMaxLength(200).IsRequired();
        builder.Property(x => x.Status).HasColumnName("status").HasMaxLength(32).IsRequired();
        builder.Property(x => x.ReconciledAmount).HasColumnName("reconciled_amount").HasColumnType("decimal(18,2)").IsRequired();
        builder.Property(x => x.ExternalReference).HasColumnName("external_reference").HasMaxLength(128);
        builder.Property(x => x.ImportSource).HasColumnName("import_source").HasMaxLength(64);
        builder.Property(x => x.CreatedUtc).HasColumnName("created_at").IsRequired();
        builder.Property(x => x.UpdatedUtc).HasColumnName("updated_at").IsRequired();

        builder.HasCheckConstraint("CK_bank_transactions_status", BankTransactionReconciliationStatuses.BuildCheckConstraintSql("status"));
        builder.HasCheckConstraint("CK_bank_transactions_reconciled_amount", "reconciled_amount >= 0 AND reconciled_amount <= ABS(amount)");

        builder.HasIndex(x => x.CompanyId);
        builder.HasIndex(x => new { x.CompanyId, x.BankAccountId, x.BookingDate });
        builder.HasIndex(x => new { x.CompanyId, x.BookingDate });
        builder.HasIndex(x => new { x.CompanyId, x.Status, x.BookingDate });
        builder.HasIndex(x => new { x.CompanyId, x.Amount });
        builder.HasIndex(x => new { x.CompanyId, x.ExternalReference }).IsUnique().HasFilter("\"external_reference\" IS NOT NULL");

        builder.HasOne(x => x.Company).WithMany().HasForeignKey(x => x.CompanyId).OnDelete(DeleteBehavior.Cascade);
        builder.HasOne(x => x.BankAccount).WithMany(x => x.Transactions).HasForeignKey(x => x.BankAccountId).OnDelete(DeleteBehavior.Restrict);
        builder.HasMany(x => x.PaymentLinks).WithOne(x => x.BankTransaction).HasForeignKey(x => x.BankTransactionId).OnDelete(DeleteBehavior.Cascade);
        builder.HasMany(x => x.CashLedgerLinks).WithOne(x => x.BankTransaction).HasForeignKey(x => x.BankTransactionId).OnDelete(DeleteBehavior.Cascade);
        builder.HasOne(x => x.PostingStateRecord)
            .WithOne(x => x.BankTransaction)
            .HasForeignKey<BankTransactionPostingStateRecord>(x => x.BankTransactionId);
    }
}

internal sealed class BankTransactionPaymentLinkEntityConfiguration : IEntityTypeConfiguration<BankTransactionPaymentLink>
{
    public void Configure(EntityTypeBuilder<BankTransactionPaymentLink> builder)
    {
        builder.ToTable("bank_transaction_payment_links");

        builder.HasKey(x => x.Id);
        builder.Property(x => x.Id).HasColumnName("id");
        builder.Property(x => x.CompanyId).HasColumnName("company_id").IsRequired();
        builder.Property(x => x.BankTransactionId).HasColumnName("bank_transaction_id").IsRequired();
        builder.Property(x => x.PaymentId).HasColumnName("payment_id").IsRequired();
        builder.Property(x => x.AllocatedAmount).HasColumnName("allocated_amount").HasColumnType("decimal(18,2)").IsRequired();
        builder.Property(x => x.Currency).HasColumnName("currency").HasMaxLength(3).IsRequired();
        builder.Property(x => x.CreatedUtc).HasColumnName("created_at").IsRequired();

        builder.HasCheckConstraint("CK_bank_transaction_payment_links_allocated_amount", "allocated_amount > 0");

        builder.HasIndex(x => new { x.CompanyId, x.BankTransactionId });
        builder.HasIndex(x => new { x.CompanyId, x.PaymentId });
        builder.HasIndex(x => new { x.CompanyId, x.BankTransactionId, x.PaymentId }).IsUnique();

        builder.HasOne(x => x.Company).WithMany().HasForeignKey(x => x.CompanyId).OnDelete(DeleteBehavior.Cascade);
        builder.HasOne(x => x.Payment).WithMany().HasForeignKey(x => x.PaymentId).OnDelete(DeleteBehavior.Restrict);
    }
}

internal sealed class BankTransactionCashLedgerLinkEntityConfiguration : IEntityTypeConfiguration<BankTransactionCashLedgerLink>
{
    public void Configure(EntityTypeBuilder<BankTransactionCashLedgerLink> builder)
    {
        builder.ToTable("bank_transaction_cash_ledger_links");

        builder.HasKey(x => x.Id);
        builder.Property(x => x.Id).HasColumnName("id");
        builder.Property(x => x.CompanyId).HasColumnName("company_id").IsRequired();
        builder.Property(x => x.BankTransactionId).HasColumnName("bank_transaction_id").IsRequired();
        builder.Property(x => x.LedgerEntryId).HasColumnName("ledger_entry_id").IsRequired();
        builder.Property(x => x.IdempotencyKey).HasColumnName("idempotency_key").HasMaxLength(160).IsRequired();
        builder.Property(x => x.CreatedUtc).HasColumnName("created_at").IsRequired();

        builder.HasIndex(x => new { x.CompanyId, x.BankTransactionId }).IsUnique();
        builder.HasIndex(x => new { x.CompanyId, x.IdempotencyKey }).IsUnique();

        builder.HasOne(x => x.Company).WithMany().HasForeignKey(x => x.CompanyId).OnDelete(DeleteBehavior.Cascade);
        builder.HasOne(x => x.LedgerEntry).WithMany().HasForeignKey(x => x.LedgerEntryId).OnDelete(DeleteBehavior.Cascade);
    }
}