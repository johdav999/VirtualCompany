using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using VirtualCompany.Domain.Entities;

namespace VirtualCompany.Infrastructure.Persistence;

internal sealed class AccountingTaxReviewConfiguration : IEntityTypeConfiguration<AccountingTaxReview>
{
    public void Configure(EntityTypeBuilder<AccountingTaxReview> builder)
    {
        builder.ToTable("accounting_tax_reviews");
        builder.HasKey(x => x.Id);
        builder.Property(x => x.Id).HasColumnName("id");
        builder.Property(x => x.CompanyId).HasColumnName("company_id").IsRequired();
        builder.Property(x => x.FiscalPeriodId).HasColumnName("fiscal_period_id").IsRequired();
        builder.Property(x => x.SummaryJson).HasColumnName("summary_json").HasColumnType("nvarchar(max)").IsRequired();
        builder.Property(x => x.Checksum).HasColumnName("checksum").HasMaxLength(64).IsRequired();
        builder.Property(x => x.ReviewedByUserId).HasColumnName("reviewed_by_user_id").IsRequired();
        builder.Property(x => x.ReviewedUtc).HasColumnName("reviewed_at").IsRequired();
        builder.HasIndex(x => new { x.CompanyId, x.FiscalPeriodId }).IsUnique();
        builder.HasOne(x => x.Company).WithMany().HasForeignKey(x => x.CompanyId).OnDelete(DeleteBehavior.Cascade);
        builder.HasOne(x => x.FiscalPeriod).WithMany().HasForeignKey(x => new { x.CompanyId, x.FiscalPeriodId })
            .HasPrincipalKey(x => new { x.CompanyId, x.Id }).OnDelete(DeleteBehavior.Restrict);
    }
}
