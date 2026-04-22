using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using VirtualCompany.Domain.Entities;
using VirtualCompany.Domain.Enums;

namespace VirtualCompany.Infrastructure.Persistence;

internal sealed class FinanceAssetEntityConfiguration : IEntityTypeConfiguration<FinanceAsset>
{
    public void Configure(EntityTypeBuilder<FinanceAsset> builder)
    {
        builder.ToTable("finance_assets");
        builder.ToTable(t =>
        {
            t.HasCheckConstraint("CK_finance_assets_amount_positive", "amount > 0");
            t.HasCheckConstraint("CK_finance_assets_funding_behavior", FinanceAssetFundingBehaviors.BuildCheckConstraintSql("funding_behavior"));
            t.HasCheckConstraint("CK_finance_assets_funding_settlement_status", FinanceSettlementStatuses.BuildCheckConstraintSql("funding_settlement_status"));
            t.HasCheckConstraint("CK_finance_assets_status", FinanceAssetStatuses.BuildCheckConstraintSql("status"));
        });

        builder.HasKey(x => x.Id);
        builder.HasAlternateKey(x => new { x.CompanyId, x.Id });
        builder.Property(x => x.Id).HasColumnName("id");
        builder.Property(x => x.CompanyId).HasColumnName("company_id").IsRequired();
        builder.Property(x => x.CounterpartyId).HasColumnName("counterparty_id").IsRequired();
        builder.Property(x => x.ReferenceNumber).HasColumnName("reference_number").HasMaxLength(64).IsRequired();
        builder.Property(x => x.Name).HasColumnName("name").HasMaxLength(160).IsRequired();
        builder.Property(x => x.Category).HasColumnName("category").HasMaxLength(64).IsRequired();
        builder.Property(x => x.PurchasedUtc).HasColumnName("purchased_at").IsRequired();
        builder.Property(x => x.Amount).HasColumnName("amount").HasColumnType("decimal(18,2)").IsRequired();
        builder.Property(x => x.Currency).HasColumnName("currency").HasMaxLength(3).IsRequired();
        builder.Property(x => x.FundingBehavior).HasColumnName("funding_behavior").HasMaxLength(32).IsRequired();
        builder.Property(x => x.FundingSettlementStatus).HasColumnName("funding_settlement_status").HasMaxLength(32).IsRequired();
        builder.Property(x => x.Status).HasColumnName("status").HasMaxLength(32).IsRequired();
        builder.Property(x => x.CreatedUtc).HasColumnName("created_at").IsRequired();
        builder.Property(x => x.UpdatedUtc).HasColumnName("updated_at").IsRequired();

        builder.HasIndex(x => new { x.CompanyId, x.ReferenceNumber }).IsUnique();
        builder.HasIndex(x => new { x.CompanyId, x.PurchasedUtc });
        builder.HasIndex(x => new { x.CompanyId, x.FundingBehavior, x.FundingSettlementStatus });

        builder.HasOne(x => x.Company).WithMany().HasForeignKey(x => x.CompanyId).OnDelete(DeleteBehavior.Cascade);
        builder.HasOne(x => x.Counterparty)
            .WithMany()
            .HasForeignKey(x => new { x.CompanyId, x.CounterpartyId })
            .HasPrincipalKey(x => new { x.CompanyId, x.Id })
            .OnDelete(DeleteBehavior.Restrict);
    }
}
