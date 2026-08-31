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
        builder.ToTable("ledger_entry_lines", table =>
        {
            table.HasCheckConstraint("CK_ledger_entry_lines_document_amount", "CAST(document_debit_amount AS NUMERIC) >= 0 AND CAST(document_credit_amount AS NUMERIC) >= 0 AND (((CAST(document_debit_amount AS NUMERIC) > 0 AND NOT(CAST(document_credit_amount AS NUMERIC) > 0)) OR (CAST(document_credit_amount AS NUMERIC) > 0 AND NOT(CAST(document_debit_amount AS NUMERIC) > 0))) OR (CAST(document_debit_amount AS NUMERIC) = 0 AND CAST(document_credit_amount AS NUMERIC) = 0))");
            table.HasCheckConstraint("CK_ledger_entry_lines_exchange_rate", "exchange_rate IS NULL OR exchange_rate > 0");
        });

        builder.HasKey(x => x.Id);
        builder.HasAlternateKey(x => new { x.CompanyId, x.Id });
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
        builder.Property(x => x.DocumentDebitAmount).HasColumnName("document_debit_amount").HasPrecision(38, 18).IsRequired();
        builder.Property(x => x.DocumentCreditAmount).HasColumnName("document_credit_amount").HasPrecision(38, 18).IsRequired();
        builder.Property(x => x.DocumentCurrency).HasColumnName("document_currency").HasMaxLength(3).IsRequired();
        builder.Property(x => x.ExchangeRate).HasColumnName("exchange_rate").HasPrecision(38, 18);
        builder.Property(x => x.ExchangeRateDate).HasColumnName("exchange_rate_date");
        builder.Property(x => x.ExchangeRateConversionId).HasColumnName("exchange_rate_conversion_id");
        builder.Property(x => x.ExchangeRateIdentity).HasColumnName("exchange_rate_identity").HasMaxLength(128);
        builder.Property(x => x.ConversionRoundingResidual).HasColumnName("conversion_rounding_residual").HasPrecision(38, 18);

        builder.HasIndex(x => new { x.CompanyId, x.FinanceAccountId, x.CostCenterId });
        builder.HasIndex(x => new { x.CompanyId, x.LedgerEntryId });
        builder.HasIndex(x => new { x.CompanyId, x.DocumentCurrency, x.ExchangeRateDate });
        builder.HasIndex(x => new { x.CompanyId, x.ExchangeRateConversionId });
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
        builder.HasOne(x => x.ExchangeRateConversion)
            .WithMany()
            .HasForeignKey(x => new { x.CompanyId, x.ExchangeRateConversionId })
            .HasPrincipalKey(x => new { x.CompanyId, x.Id })
            .OnDelete(DeleteBehavior.Restrict);
    }
}

