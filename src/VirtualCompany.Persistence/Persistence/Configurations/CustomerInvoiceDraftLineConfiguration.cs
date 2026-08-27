using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using VirtualCompany.Domain.Entities;

namespace VirtualCompany.Infrastructure.Persistence.Configurations;

public sealed class CustomerInvoiceDraftLineConfiguration : IEntityTypeConfiguration<CustomerInvoiceDraftLine>
{
    public void Configure(EntityTypeBuilder<CustomerInvoiceDraftLine> builder)
    {
        builder.ToTable("customer_invoice_draft_lines");
        builder.HasKey(x => x.Id);
        builder.Property(x => x.Id).HasColumnName("id");
        builder.Property(x => x.CompanyId).HasColumnName("company_id").IsRequired();
        builder.Property(x => x.DraftId).HasColumnName("draft_id").IsRequired();
        builder.Property(x => x.Sequence).HasColumnName("sequence").IsRequired();
        builder.Property(x => x.Description).HasColumnName("description").HasMaxLength(500).IsRequired();
        builder.Property(x => x.Quantity).HasColumnName("quantity").HasPrecision(19, 6).IsRequired();
        builder.Property(x => x.Unit).HasColumnName("unit").HasMaxLength(32).IsRequired();
        builder.Property(x => x.UnitPrice).HasColumnName("unit_price").HasPrecision(19, 6).IsRequired();
        builder.Property(x => x.DiscountPercent).HasColumnName("discount_percent").HasPrecision(9, 6).IsRequired();
        builder.Property(x => x.DiscountAmount).HasColumnName("discount_amount").HasPrecision(19, 6).IsRequired();
        builder.Property(x => x.NetAmount).HasColumnName("net_amount").HasPrecision(19, 6).IsRequired();
        builder.Property(x => x.TaxRuleKey).HasColumnName("tax_rule_key").HasMaxLength(100).IsRequired();
        builder.Property(x => x.TaxRuleVersion).HasColumnName("tax_rule_version").HasMaxLength(64).IsRequired();
        builder.Property(x => x.TaxClassification).HasColumnName("tax_classification").HasMaxLength(100).IsRequired();
        builder.Property(x => x.TaxRate).HasColumnName("tax_rate").HasPrecision(9, 6).IsRequired();
        builder.Property(x => x.TaxAmount).HasColumnName("tax_amount").HasPrecision(19, 6).IsRequired();
        builder.Property(x => x.GrossAmount).HasColumnName("gross_amount").HasPrecision(19, 6).IsRequired();
        builder.Property(x => x.RevenueAccountRoleKey).HasColumnName("revenue_account_role_key").HasMaxLength(100);
        builder.Property(x => x.TaxAccountRoleKey).HasColumnName("tax_account_role_key").HasMaxLength(100);
        builder.Property(x => x.VatBoxMappingsJson).HasColumnName("vat_box_mappings_json").HasMaxLength(2000).IsRequired();
        builder.Property(x => x.TaxEvidenceJson).HasColumnName("tax_evidence_json").HasMaxLength(8000).IsRequired();
        builder.Property(x => x.DimensionFactsJson).HasColumnName("dimension_facts_json").HasMaxLength(8000).IsRequired();
        builder.Property(x => x.SourceReference).HasColumnName("source_reference").HasMaxLength(200);
        builder.Property(x => x.OrderReference).HasColumnName("order_reference").HasMaxLength(200);
        builder.HasIndex(x => new { x.CompanyId, x.DraftId, x.Sequence }).IsUnique();
        builder.HasOne(x => x.Draft).WithMany(x => x.Lines)
            .HasForeignKey(x => new { x.CompanyId, x.DraftId }).HasPrincipalKey(x => new { x.CompanyId, x.Id })
            .OnDelete(DeleteBehavior.Cascade);
    }
}
