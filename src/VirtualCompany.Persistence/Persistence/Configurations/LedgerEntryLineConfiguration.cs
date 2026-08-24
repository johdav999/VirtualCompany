using System.Text.Json;
using System.Text.Json.Nodes;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using Microsoft.EntityFrameworkCore.ChangeTracking;
using Microsoft.EntityFrameworkCore.Storage.ValueConversion;
using VirtualCompany.Domain.Entities;
using VirtualCompany.Domain.Enums;

namespace VirtualCompany.Infrastructure.Persistence;
internal sealed class LedgerEntryLineConfiguration : IEntityTypeConfiguration<LedgerEntryLine>
{
    public void Configure(EntityTypeBuilder<LedgerEntryLine> builder)
    {
        builder.ToTable("ledger_entry_lines");

        builder.HasKey(x => x.Id);
        builder.Property(x => x.Id).HasColumnName("id");
        builder.Property(x => x.CompanyId).HasColumnName("company_id").IsRequired();
        builder.Property(x => x.LedgerEntryId).HasColumnName("ledger_entry_id").IsRequired();
        builder.Property(x => x.FinanceAccountId).HasColumnName("finance_account_id").IsRequired();
        builder.Property(x => x.CostCenterId).HasColumnName("cost_center_id");
        builder.Property(x => x.DebitAmount).HasColumnName("debit_amount").HasColumnType("decimal(18,2)").IsRequired();
        builder.Property(x => x.CreditAmount).HasColumnName("credit_amount").HasColumnType("decimal(18,2)").IsRequired();
        builder.Property(x => x.Currency).HasColumnName("currency").HasMaxLength(3).IsRequired();
        builder.Property(x => x.Description).HasColumnName("description").HasMaxLength(500);
        builder.Property(x => x.CreatedUtc).HasColumnName("created_at").IsRequired();
        builder.Property(x => x.TaxFactsJson).HasColumnName("tax_facts_json").HasMaxLength(8000);
        builder.Property(x => x.DimensionFactsJson).HasColumnName("dimension_facts_json").HasMaxLength(8000);

        builder.HasIndex(x => new { x.CompanyId, x.FinanceAccountId, x.CostCenterId });
        builder.HasIndex(x => new { x.CompanyId, x.LedgerEntryId });
        builder.HasIndex(x => new { x.CompanyId, x.FinanceAccountId })
            .HasAnnotation("SqlServer:Include",
                new[] { nameof(LedgerEntryLine.LedgerEntryId), nameof(LedgerEntryLine.DebitAmount), nameof(LedgerEntryLine.CreditAmount) });
        builder.HasOne(x => x.Company).WithMany().HasForeignKey(x => x.CompanyId).OnDelete(DeleteBehavior.Cascade);
        builder.HasOne(x => x.LedgerEntry)
            .WithMany(x => x.Lines)
            .HasForeignKey(x => new { x.CompanyId, x.LedgerEntryId })
            .HasPrincipalKey(x => new { x.CompanyId, x.Id })
            .OnDelete(DeleteBehavior.NoAction);
        builder.HasOne(x => x.FinanceAccount)
            .WithMany()
            .HasForeignKey(x => new { x.CompanyId, x.FinanceAccountId })
            .HasPrincipalKey(x => new { x.CompanyId, x.Id })
            .OnDelete(DeleteBehavior.Restrict);
    }
}

