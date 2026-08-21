using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using VirtualCompany.Domain.Entities;
using VirtualCompany.Domain.Enums;

namespace VirtualCompany.Infrastructure.Persistence;

internal sealed class BankReconciliationFollowUpConfiguration : IEntityTypeConfiguration<BankReconciliationFollowUp>
{
    public void Configure(EntityTypeBuilder<BankReconciliationFollowUp> builder)
    {
        builder.ToTable("bank_reconciliation_follow_ups", table =>
            table.HasCheckConstraint("CK_bank_reconciliation_follow_ups_status", BankReconciliationFollowUpStatuses.BuildCheckConstraintSql("status")));
        builder.HasKey(x => x.Id);
        builder.Property(x => x.Id).HasColumnName("id");
        builder.Property(x => x.CompanyId).HasColumnName("company_id").IsRequired();
        builder.Property(x => x.BankTransactionId).HasColumnName("bank_transaction_id").IsRequired();
        builder.Property(x => x.LedgerEntryId).HasColumnName("ledger_entry_id").IsRequired();
        builder.Property(x => x.Status).HasColumnName("status").HasMaxLength(32).IsRequired();
        builder.Property(x => x.Reason).HasColumnName("reason").HasMaxLength(512).IsRequired();
        builder.Property(x => x.CreatedByUserId).HasColumnName("created_by_user_id").IsRequired();
        builder.Property(x => x.CreatedUtc).HasColumnName("created_at").IsRequired();
        builder.Property(x => x.ResolvedByUserId).HasColumnName("resolved_by_user_id");
        builder.Property(x => x.ResolvedUtc).HasColumnName("resolved_at");
        builder.Property(x => x.ResolutionLedgerEntryId).HasColumnName("resolution_ledger_entry_id");
        builder.HasIndex(x => new { x.CompanyId, x.BankTransactionId, x.Status });
        builder.HasIndex(x => new { x.CompanyId, x.BankTransactionId }).IsUnique().HasFilter("status = 'open'");
        builder.HasOne(x => x.Company).WithMany().HasForeignKey(x => x.CompanyId).OnDelete(DeleteBehavior.Cascade);
        builder.HasOne(x => x.BankTransaction).WithMany()
            .HasForeignKey(x => new { x.CompanyId, x.BankTransactionId })
            .HasPrincipalKey(x => new { x.CompanyId, x.Id })
            .OnDelete(DeleteBehavior.NoAction);
        builder.HasOne(x => x.LedgerEntry).WithMany()
            .HasForeignKey(x => new { x.CompanyId, x.LedgerEntryId })
            .HasPrincipalKey(x => new { x.CompanyId, x.Id })
            .OnDelete(DeleteBehavior.NoAction);
    }
}
