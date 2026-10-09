using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using VirtualCompany.Domain.Entities;
namespace VirtualCompany.Infrastructure.Persistence;

internal sealed class FinanceForecastRevisionConfiguration : IEntityTypeConfiguration<FinanceForecastRevision>
{
    public void Configure(EntityTypeBuilder<FinanceForecastRevision> b)
    {
        b.ToTable("finance_forecast_revisions"); b.HasKey(x => x.Id); b.HasAlternateKey(x => new { x.CompanyId, x.Id });
        b.Property(x => x.Name).HasMaxLength(64).IsRequired(); b.Property(x => x.NativeVersion).HasMaxLength(64).IsRequired();
        b.Property(x => x.Payload).HasColumnType("nvarchar(max)").IsRequired(); b.Property(x => x.Checksum).HasMaxLength(64).IsRequired();
        b.Property(x => x.CommandHash).HasMaxLength(64).IsRequired();
        b.HasIndex(x => new { x.CompanyId, x.AuthorId, x.RequestId }).IsUnique();
        b.HasIndex(x => new { x.CompanyId, x.NativeVersion }).IsUnique();
        b.HasIndex(x => new { x.CompanyId, x.PreviousId }).IsUnique().HasFilter("[PreviousId] IS NOT NULL");
        b.HasIndex(x => new { x.CompanyId, x.SavedUtc });
        b.HasOne<Company>().WithMany().HasForeignKey(x => x.CompanyId).OnDelete(DeleteBehavior.Cascade);
        b.HasOne<FinanceForecastRevision>().WithMany().HasForeignKey(x => new { x.CompanyId, x.PreviousId })
            .HasPrincipalKey(x => new { x.CompanyId, x.Id }).OnDelete(DeleteBehavior.Restrict);
    }
}
internal sealed class FinanceVarianceExplanationConfiguration : IEntityTypeConfiguration<FinanceVarianceExplanation>
{
    public void Configure(EntityTypeBuilder<FinanceVarianceExplanation> b)
    {
        b.ToTable("finance_variance_explanations"); b.HasKey(x => x.Id);
        b.Property(x => x.Currency).HasMaxLength(3).IsRequired(); b.Property(x => x.BudgetVersion).HasMaxLength(64);
        b.Property(x => x.Text).HasMaxLength(2000).IsRequired(); b.Property(x => x.SourceFingerprint).HasMaxLength(64).IsRequired();
        b.HasIndex(x => new { x.CompanyId, x.AuthorId, x.RequestId }).IsUnique(); b.HasIndex(x => new { x.CompanyId, x.MonthUtc });
        b.HasOne<Company>().WithMany().HasForeignKey(x => x.CompanyId).OnDelete(DeleteBehavior.Cascade);
        b.HasOne<FinanceAccount>().WithMany().HasForeignKey(x => new { x.CompanyId, x.AccountId })
            .HasPrincipalKey(x => new { x.CompanyId, x.Id }).OnDelete(DeleteBehavior.Restrict);
    }
}
