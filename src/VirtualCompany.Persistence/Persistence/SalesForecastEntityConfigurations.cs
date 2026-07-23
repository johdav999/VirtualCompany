using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using VirtualCompany.Domain.Entities;

namespace VirtualCompany.Infrastructure.Persistence;

internal sealed class RevenueForecastSnapshotConfiguration : IEntityTypeConfiguration<RevenueForecastSnapshot>
{
    public void Configure(EntityTypeBuilder<RevenueForecastSnapshot> builder)
    {
        builder.ToTable("revenue_forecast_snapshots");
        builder.HasKey(x => x.Id);
        builder.HasAlternateKey(x => new { x.CompanyId, x.Id });
        builder.Property(x => x.Id).HasColumnName("id");
        builder.Property(x => x.CompanyId).HasColumnName("company_id").IsRequired();
        builder.Property(x => x.AsOfUtc).HasColumnName("as_of_at").IsRequired();
        builder.Property(x => x.Currency).HasColumnName("currency").HasMaxLength(3).IsRequired();
        builder.Property(x => x.GrossPipeline30Days).HasColumnName("gross_pipeline_30_days").HasPrecision(18, 2).IsRequired();
        builder.Property(x => x.ExpectedRevenue30Days).HasColumnName("expected_revenue_30_days").HasPrecision(18, 2).IsRequired();
        builder.Property(x => x.DealCount30Days).HasColumnName("deal_count_30_days").IsRequired();
        builder.Property(x => x.GrossPipeline60Days).HasColumnName("gross_pipeline_60_days").HasPrecision(18, 2).IsRequired();
        builder.Property(x => x.ExpectedRevenue60Days).HasColumnName("expected_revenue_60_days").HasPrecision(18, 2).IsRequired();
        builder.Property(x => x.DealCount60Days).HasColumnName("deal_count_60_days").IsRequired();
        builder.Property(x => x.GrossPipeline90Days).HasColumnName("gross_pipeline_90_days").HasPrecision(18, 2).IsRequired();
        builder.Property(x => x.ExpectedRevenue90Days).HasColumnName("expected_revenue_90_days").HasPrecision(18, 2).IsRequired();
        builder.Property(x => x.DealCount90Days).HasColumnName("deal_count_90_days").IsRequired();
        builder.Property(x => x.UnknownRiskDeals).HasColumnName("unknown_risk_deals").IsRequired();
        builder.Property(x => x.LowRiskDeals).HasColumnName("low_risk_deals").IsRequired();
        builder.Property(x => x.MediumRiskDeals).HasColumnName("medium_risk_deals").IsRequired();
        builder.Property(x => x.HighRiskDeals).HasColumnName("high_risk_deals").IsRequired();
        builder.Property(x => x.CalculatedUtc).HasColumnName("calculated_at").IsRequired();

        builder.HasIndex(x => new { x.CompanyId, x.CalculatedUtc });
        builder.HasIndex(x => new { x.CompanyId, x.AsOfUtc }).IsUnique();
        builder.HasOne(x => x.Company).WithMany().HasForeignKey(x => x.CompanyId).OnDelete(DeleteBehavior.Cascade);

        builder.ToTable(t =>
        {
            t.HasCheckConstraint("CK_revenue_forecast_snapshots_deal_counts_nonnegative", "deal_count_30_days >= 0 AND deal_count_60_days >= 0 AND deal_count_90_days >= 0");
            t.HasCheckConstraint("CK_revenue_forecast_snapshots_risk_counts_nonnegative", "unknown_risk_deals >= 0 AND low_risk_deals >= 0 AND medium_risk_deals >= 0 AND high_risk_deals >= 0");
        });
    }
}

internal sealed class DealRiskScoreSnapshotConfiguration : IEntityTypeConfiguration<DealRiskScoreSnapshot>
{
    public void Configure(EntityTypeBuilder<DealRiskScoreSnapshot> builder)
    {
        builder.ToTable("deal_risk_score_snapshots");
        builder.HasKey(x => x.Id);
        builder.HasAlternateKey(x => new { x.CompanyId, x.Id });
        builder.Property(x => x.Id).HasColumnName("id");
        builder.Property(x => x.CompanyId).HasColumnName("company_id").IsRequired();
        builder.Property(x => x.DealId).HasColumnName("deal_id").IsRequired();
        builder.Property(x => x.ScoreDateUtc).HasColumnName("score_date").IsRequired();
        builder.Property(x => x.Score).HasColumnName("score").HasPrecision(6, 4).IsRequired();
        builder.Property(x => x.Band).HasColumnName("band").HasMaxLength(32).IsRequired();
        builder.Property(x => x.FactorsSummary).HasColumnName("factors_summary").HasMaxLength(1000).IsRequired();
        builder.Property(x => x.CalculatedUtc).HasColumnName("calculated_at").IsRequired();

        builder.HasIndex(x => new { x.CompanyId, x.DealId });
        builder.HasIndex(x => new { x.CompanyId, x.CalculatedUtc });
        builder.HasIndex(x => new { x.CompanyId, x.DealId, x.ScoreDateUtc }).IsUnique();
        builder.HasIndex(x => new { x.CompanyId, x.Band, x.ScoreDateUtc });
        builder.HasOne(x => x.Deal)
            .WithMany()
            .HasForeignKey(nameof(DealRiskScoreSnapshot.CompanyId), nameof(DealRiskScoreSnapshot.DealId))
            .HasPrincipalKey(nameof(Deal.CompanyId), nameof(Deal.Id))
            .OnDelete(DeleteBehavior.Restrict);

        builder.ToTable(t =>
        {
            t.HasCheckConstraint("CK_deal_risk_score_snapshots_score_range", "score >= 0 AND score <= 1");
        });
    }
}