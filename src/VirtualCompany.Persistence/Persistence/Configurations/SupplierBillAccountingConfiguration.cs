using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using VirtualCompany.Domain.Entities;

namespace VirtualCompany.Infrastructure.Persistence;

internal sealed class SupplierBillAccountingProfileConfiguration : IEntityTypeConfiguration<SupplierBillAccountingProfile>
{
    public void Configure(EntityTypeBuilder<SupplierBillAccountingProfile> builder)
    {
        builder.ToTable("supplier_bill_accounting_profiles", table =>
        {
            table.HasCheckConstraint("CK_supplier_bill_accounting_status", "status IN ('not_ready','awaiting_approval','ready_to_post','posted','reversed','blocked')");
            table.HasCheckConstraint("CK_supplier_bill_accounting_exchange_rate", "exchange_rate > 0");
            table.HasCheckConstraint("CK_supplier_bill_accounting_amounts", "net_amount >= 0 AND recoverable_tax_amount >= 0 AND non_recoverable_tax_amount >= 0 AND gross_amount > 0 AND gross_base_amount > 0");
        });
        builder.HasKey(x => x.Id);
        builder.HasAlternateKey(x => new { x.CompanyId, x.Id });
        builder.Property(x => x.Id).HasColumnName("id");
        builder.Property(x => x.CompanyId).HasColumnName("company_id").IsRequired();
        builder.Property(x => x.BillId).HasColumnName("bill_id").IsRequired();
        builder.Property(x => x.FiscalPeriodId).HasColumnName("fiscal_period_id").IsRequired();
        builder.Property(x => x.VoucherSeriesCode).HasColumnName("voucher_series_code").HasMaxLength(32).IsRequired();
        builder.Property(x => x.DocumentCurrency).HasColumnName("document_currency").HasMaxLength(3).IsRequired();
        builder.Property(x => x.BaseCurrency).HasColumnName("base_currency").HasMaxLength(3).IsRequired();
        builder.Property(x => x.ExchangeRate).HasColumnName("exchange_rate").HasColumnType("decimal(19,8)").IsRequired();
        builder.Property(x => x.NetAmount).HasColumnName("net_amount").HasColumnType("decimal(19,6)").IsRequired();
        builder.Property(x => x.RecoverableTaxAmount).HasColumnName("recoverable_tax_amount").HasColumnType("decimal(19,6)").IsRequired();
        builder.Property(x => x.NonRecoverableTaxAmount).HasColumnName("non_recoverable_tax_amount").HasColumnType("decimal(19,6)").IsRequired();
        builder.Property(x => x.GrossAmount).HasColumnName("gross_amount").HasColumnType("decimal(19,6)").IsRequired();
        builder.Property(x => x.CostBaseAmount).HasColumnName("cost_base_amount").HasColumnType("decimal(19,6)").IsRequired();
        builder.Property(x => x.RecoverableTaxBaseAmount).HasColumnName("recoverable_tax_base_amount").HasColumnType("decimal(19,6)").IsRequired();
        builder.Property(x => x.GrossBaseAmount).HasColumnName("gross_base_amount").HasColumnType("decimal(19,6)").IsRequired();
        builder.Property(x => x.RoundingBaseAmount).HasColumnName("rounding_base_amount").HasColumnType("decimal(19,6)").IsRequired();
        builder.Property(x => x.ExchangeRateConversionId).HasColumnName("exchange_rate_conversion_id");
        builder.Property(x => x.ExchangeRateDate).HasColumnName("exchange_rate_date");
        builder.Property(x => x.ExchangeRatePurpose).HasColumnName("exchange_rate_purpose").HasMaxLength(32);
        builder.Property(x => x.ExchangeRateIdentity).HasColumnName("exchange_rate_identity").HasMaxLength(128);
        builder.Property(x => x.ConversionRoundingResidual).HasColumnName("conversion_rounding_residual").HasPrecision(38, 18);
        builder.Property(x => x.CurrencyProvenance).HasColumnName("currency_provenance").HasMaxLength(32).IsRequired();
        builder.Ignore(x => x.HasAuthoritativeCurrencyFacts);
        builder.Property(x => x.PayableAccountId).HasColumnName("payable_account_id").IsRequired();
        builder.Property(x => x.TaxTreatment).HasColumnName("tax_treatment").HasMaxLength(32).IsRequired();
        builder.Property(x => x.PolicyPackKey).HasColumnName("policy_pack_key").HasMaxLength(96).IsRequired();
        builder.Property(x => x.PolicyPackVersion).HasColumnName("policy_pack_version").HasMaxLength(32).IsRequired();
        builder.Property(x => x.PolicyDefinitionHash).HasColumnName("policy_definition_hash").HasMaxLength(64).IsRequired();
        builder.Property(x => x.SourceDocumentHash).HasColumnName("source_document_hash").HasMaxLength(64);
        builder.Property(x => x.Status).HasColumnName("status").HasMaxLength(32).IsRequired();
        builder.Property(x => x.PayloadHash).HasColumnName("payload_hash").HasMaxLength(64).IsRequired();
        builder.Property(x => x.Version).HasColumnName("version").IsConcurrencyToken().IsRequired();
        builder.Property(x => x.ApprovalRequestId).HasColumnName("approval_request_id");
        builder.Property(x => x.LedgerEntryId).HasColumnName("ledger_entry_id");
        builder.Property(x => x.OriginalBillId).HasColumnName("original_bill_id");
        builder.Property(x => x.BlockingReasonCode).HasColumnName("blocking_reason_code").HasMaxLength(96);
        builder.Property(x => x.BlockingReason).HasColumnName("blocking_reason").HasMaxLength(1000);
        builder.Property(x => x.CreatedByUserId).HasColumnName("created_by_user_id").IsRequired();
        builder.Property(x => x.UpdatedByUserId).HasColumnName("updated_by_user_id").IsRequired();
        builder.Property(x => x.CreatedUtc).HasColumnName("created_utc").IsRequired();
        builder.Property(x => x.UpdatedUtc).HasColumnName("updated_utc").IsRequired();
        builder.HasIndex(x => new { x.CompanyId, x.BillId }).IsUnique();
        builder.HasIndex(x => new { x.CompanyId, x.Status, x.UpdatedUtc });
        builder.HasIndex(x => new { x.CompanyId, x.LedgerEntryId }).IsUnique().HasFilter("[ledger_entry_id] IS NOT NULL");
        builder.HasIndex(x => new { x.CompanyId, x.ExchangeRateConversionId });
        builder.HasIndex(x => new { x.CompanyId, x.DocumentCurrency, x.ExchangeRateDate });
        builder.HasOne(x => x.Bill).WithOne().HasForeignKey<SupplierBillAccountingProfile>(x => new { x.CompanyId, x.BillId })
            .HasPrincipalKey<FinanceBill>(x => new { x.CompanyId, x.Id }).OnDelete(DeleteBehavior.Restrict);
        builder.HasOne(x => x.OriginalBill).WithMany().HasForeignKey(x => new { x.CompanyId, x.OriginalBillId })
            .HasPrincipalKey(x => new { x.CompanyId, x.Id }).OnDelete(DeleteBehavior.Restrict);
        builder.HasOne(x => x.ApprovalRequest).WithMany().HasForeignKey(x => new { x.CompanyId, x.ApprovalRequestId })
            .HasPrincipalKey(x => new { x.CompanyId, x.Id }).OnDelete(DeleteBehavior.Restrict);
        builder.HasOne(x => x.LedgerEntry).WithMany().HasForeignKey(x => new { x.CompanyId, x.LedgerEntryId })
            .HasPrincipalKey(x => new { x.CompanyId, x.Id }).OnDelete(DeleteBehavior.Restrict);
        builder.HasOne(x => x.ExchangeRateConversion).WithMany().HasForeignKey(x => new { x.CompanyId, x.ExchangeRateConversionId })
            .HasPrincipalKey(x => new { x.CompanyId, x.Id }).OnDelete(DeleteBehavior.Restrict);
        builder.HasOne<FinanceAccount>().WithMany().HasForeignKey(x => new { x.CompanyId, x.PayableAccountId })
            .HasPrincipalKey(x => new { x.CompanyId, x.Id }).OnDelete(DeleteBehavior.Restrict);
    }
}

internal sealed class SupplierBillAccountingLineConfiguration : IEntityTypeConfiguration<SupplierBillAccountingLine>
{
    public void Configure(EntityTypeBuilder<SupplierBillAccountingLine> builder)
    {
        builder.ToTable("supplier_bill_accounting_lines", table =>
            table.HasCheckConstraint("CK_supplier_bill_accounting_line_amounts", "net_amount >= 0 AND tax_amount >= 0 AND recoverable_tax_amount >= 0 AND non_recoverable_tax_amount >= 0 AND gross_amount > 0"));
        builder.HasKey(x => x.Id);
        builder.HasAlternateKey(x => new { x.CompanyId, x.Id });
        builder.Property(x => x.Id).HasColumnName("id");
        builder.Property(x => x.CompanyId).HasColumnName("company_id").IsRequired();
        builder.Property(x => x.ProfileId).HasColumnName("profile_id").IsRequired();
        builder.Property(x => x.Sequence).HasColumnName("sequence").IsRequired();
        builder.Property(x => x.Description).HasColumnName("description").HasMaxLength(500).IsRequired();
        builder.Property(x => x.CostAccountId).HasColumnName("cost_account_id").IsRequired();
        builder.Property(x => x.AccountClassification).HasColumnName("account_classification").HasMaxLength(32).IsRequired();
        builder.Property(x => x.TaxRuleKey).HasColumnName("tax_rule_key").HasMaxLength(96).IsRequired();
        builder.Property(x => x.TaxMethod).HasColumnName("tax_method").HasMaxLength(32).IsRequired();
        builder.Property(x => x.TaxTreatment).HasColumnName("tax_treatment").HasMaxLength(32).IsRequired();
        builder.Property(x => x.TaxRate).HasColumnName("tax_rate").HasColumnType("decimal(9,6)").IsRequired();
        builder.Property(x => x.NetAmount).HasColumnName("net_amount").HasColumnType("decimal(19,6)").IsRequired();
        builder.Property(x => x.TaxAmount).HasColumnName("tax_amount").HasColumnType("decimal(19,6)").IsRequired();
        builder.Property(x => x.RecoverableTaxAmount).HasColumnName("recoverable_tax_amount").HasColumnType("decimal(19,6)").IsRequired();
        builder.Property(x => x.NonRecoverableTaxAmount).HasColumnName("non_recoverable_tax_amount").HasColumnType("decimal(19,6)").IsRequired();
        builder.Property(x => x.GrossAmount).HasColumnName("gross_amount").HasColumnType("decimal(19,6)").IsRequired();
        builder.Property(x => x.CostBaseAmount).HasColumnName("cost_base_amount").HasColumnType("decimal(19,6)").IsRequired();
        builder.Property(x => x.RecoverableTaxBaseAmount).HasColumnName("recoverable_tax_base_amount").HasColumnType("decimal(19,6)").IsRequired();
        builder.Property(x => x.RecoverableTaxAccountId).HasColumnName("recoverable_tax_account_id");
        builder.Property(x => x.TaxFactsJson).HasColumnName("tax_facts_json").HasMaxLength(8000).IsRequired();
        builder.HasIndex(x => new { x.CompanyId, x.ProfileId, x.Sequence }).IsUnique();
        builder.HasOne(x => x.Profile).WithMany(x => x.Lines).HasForeignKey(x => new { x.CompanyId, x.ProfileId })
            .HasPrincipalKey(x => new { x.CompanyId, x.Id }).OnDelete(DeleteBehavior.Cascade);
        builder.HasOne<FinanceAccount>().WithMany().HasForeignKey(x => new { x.CompanyId, x.CostAccountId })
            .HasPrincipalKey(x => new { x.CompanyId, x.Id }).OnDelete(DeleteBehavior.Restrict);
        builder.HasOne<FinanceAccount>().WithMany().HasForeignKey(x => new { x.CompanyId, x.RecoverableTaxAccountId })
            .HasPrincipalKey(x => new { x.CompanyId, x.Id }).OnDelete(DeleteBehavior.Restrict);
    }
}
