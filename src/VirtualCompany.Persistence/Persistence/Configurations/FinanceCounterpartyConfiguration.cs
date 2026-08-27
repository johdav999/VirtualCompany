using System.Text.Json;
using System.Text.Json.Nodes;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using Microsoft.EntityFrameworkCore.ChangeTracking;
using Microsoft.EntityFrameworkCore.Storage.ValueConversion;
using VirtualCompany.Domain.Entities;
using VirtualCompany.Domain.Enums;

namespace VirtualCompany.Infrastructure.Persistence;
internal sealed class FinanceCounterpartyConfiguration : IEntityTypeConfiguration<FinanceCounterparty>
{
    public void Configure(EntityTypeBuilder<FinanceCounterparty> builder)
    {
        builder.ToTable("finance_counterparties");

        builder.HasKey(x => x.Id);
        builder.HasAlternateKey(x => new { x.CompanyId, x.Id });
        builder.Property(x => x.Id).HasColumnName("id");
        builder.Property(x => x.CompanyId).HasColumnName("company_id").IsRequired();
        builder.Property(x => x.Name).HasColumnName("name").HasMaxLength(200).IsRequired();
        builder.Property(x => x.CounterpartyType).HasColumnName("counterparty_type").HasMaxLength(64).IsRequired();
        builder.Property(x => x.Email).HasColumnName("email").HasMaxLength(256);
        builder.Property(x => x.PaymentTerms).HasColumnName("payment_terms").HasMaxLength(64);
        builder.Property(x => x.TaxId).HasColumnName("tax_id").HasMaxLength(64);
        builder.Property(x => x.CreditLimit).HasColumnName("credit_limit").HasColumnType("decimal(18,2)");
        builder.Property(x => x.PreferredPaymentMethod).HasColumnName("preferred_payment_method").HasMaxLength(64);
        builder.Property(x => x.DefaultAccountMapping).HasColumnName("default_account_mapping").HasMaxLength(64);
        builder.Property(x => x.MergedIntoCounterpartyId).HasColumnName("merged_into_counterparty_id");
        builder.Property(x => x.MergedUtc).HasColumnName("merged_at");
        builder.Property(x => x.CreatedUtc).HasColumnName("created_at").IsRequired();
        builder.Property(x => x.UpdatedUtc).HasColumnName("updated_at").IsRequired();

        builder.HasIndex(x => new { x.CompanyId, x.Name });
        builder.HasIndex(x => new { x.CompanyId, x.CounterpartyType, x.Name });
        builder.HasIndex(x => new { x.CompanyId, x.Email });
        builder.HasIndex(x => new { x.CompanyId, x.CounterpartyType });
        builder.HasIndex(x => new { x.CompanyId, x.MergedIntoCounterpartyId });
        builder.HasOne(x => x.Company).WithMany().HasForeignKey(x => x.CompanyId).OnDelete(DeleteBehavior.Cascade);
    }
}

