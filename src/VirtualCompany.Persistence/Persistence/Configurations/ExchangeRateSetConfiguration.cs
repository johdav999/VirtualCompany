using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using VirtualCompany.Domain.Entities;

namespace VirtualCompany.Infrastructure.Persistence.Configurations;

public sealed class ExchangeRateSetConfiguration : IEntityTypeConfiguration<ExchangeRateSet>
{
    public void Configure(EntityTypeBuilder<ExchangeRateSet> builder)
    {
        builder.ToTable("exchange_rate_sets");
        builder.HasKey(x => x.Id);
        builder.HasAlternateKey(x => new { x.CompanyId, x.Id });
        builder.Property(x => x.Id).HasColumnName("id");
        builder.Property(x => x.CompanyId).HasColumnName("company_id");
        builder.Property(x => x.SourceId).HasColumnName("source_id");
        builder.Property(x => x.SetVersion).HasColumnName("set_version");
        builder.Property(x => x.ImportIdentity).HasColumnName("import_identity").HasMaxLength(200).IsRequired();
        builder.Property(x => x.ContentHash).HasColumnName("content_hash").HasMaxLength(64).IsRequired();
        builder.Property(x => x.Status).HasColumnName("status").HasMaxLength(32).IsRequired();
        builder.Property(x => x.EffectiveFrom).HasColumnName("effective_from");
        builder.Property(x => x.EffectiveThrough).HasColumnName("effective_through");
        builder.Property(x => x.PublishedUtc).HasColumnName("published_at");
        builder.Property(x => x.ImportedByUserId).HasColumnName("imported_by_user_id");
        builder.Property(x => x.ApprovedByUserId).HasColumnName("approved_by_user_id");
        builder.Property(x => x.CorrectsRateSetId).HasColumnName("corrects_rate_set_id");
        builder.Property(x => x.ApprovedUtc).HasColumnName("approved_at");
        builder.Property(x => x.ReviewNote).HasColumnName("review_note").HasMaxLength(1000);
        builder.Property(x => x.Version).HasColumnName("version").IsConcurrencyToken();
        builder.Property(x => x.CreatedUtc).HasColumnName("created_at");
        builder.HasIndex(x => new { x.CompanyId, x.SourceId, x.ImportIdentity }).IsUnique();
        builder.HasIndex(x => new { x.CompanyId, x.SourceId, x.SetVersion }).IsUnique();
        builder.HasIndex(x => new { x.CompanyId, x.Status, x.EffectiveFrom, x.EffectiveThrough });
        builder.HasOne(x => x.Source).WithMany(x => x.RateSets)
            .HasForeignKey(x => new { x.CompanyId, x.SourceId })
            .HasPrincipalKey(x => new { x.CompanyId, x.Id }).OnDelete(DeleteBehavior.Restrict);
        builder.HasOne<ExchangeRateSet>().WithMany()
            .HasForeignKey(x => new { x.CompanyId, x.CorrectsRateSetId })
            .HasPrincipalKey(x => new { x.CompanyId, x.Id }).OnDelete(DeleteBehavior.NoAction);
    }
}

public sealed class ExchangeRateObservationConfiguration : IEntityTypeConfiguration<ExchangeRateObservation>
{
    public void Configure(EntityTypeBuilder<ExchangeRateObservation> builder)
    {
        builder.ToTable("exchange_rate_observations");
        builder.HasKey(x => x.Id);
        builder.HasAlternateKey(x => new { x.CompanyId, x.Id });
        builder.Property(x => x.Id).HasColumnName("id");
        builder.Property(x => x.CompanyId).HasColumnName("company_id");
        builder.Property(x => x.RateSetId).HasColumnName("rate_set_id");
        builder.Property(x => x.BaseCurrency).HasColumnName("base_currency").HasMaxLength(3).IsRequired();
        builder.Property(x => x.QuoteCurrency).HasColumnName("quote_currency").HasMaxLength(3).IsRequired();
        builder.Property(x => x.Rate).HasColumnName("rate").HasPrecision(38, 18);
        builder.Property(x => x.RatePrecision).HasColumnName("rate_precision");
        builder.Property(x => x.QuotationConvention).HasColumnName("quotation_convention").HasMaxLength(32).IsRequired();
        builder.Property(x => x.EffectiveDate).HasColumnName("effective_date");
        builder.Property(x => x.ObservedUtc).HasColumnName("observed_at");
        builder.Property(x => x.CorrectsObservationId).HasColumnName("corrects_observation_id");
        builder.HasIndex(x => new { x.CompanyId, x.BaseCurrency, x.QuoteCurrency, x.EffectiveDate });
        builder.HasIndex(x => new { x.CompanyId, x.RateSetId, x.BaseCurrency, x.QuoteCurrency, x.EffectiveDate }).IsUnique();
        builder.HasOne(x => x.RateSet).WithMany(x => x.Observations)
            .HasForeignKey(x => new { x.CompanyId, x.RateSetId })
            .HasPrincipalKey(x => new { x.CompanyId, x.Id }).OnDelete(DeleteBehavior.Cascade);
        builder.HasOne<ExchangeRateObservation>().WithMany()
            .HasForeignKey(x => new { x.CompanyId, x.CorrectsObservationId })
            .HasPrincipalKey(x => new { x.CompanyId, x.Id }).OnDelete(DeleteBehavior.NoAction);
    }
}

public sealed class ExchangeRateEvidenceConfiguration : IEntityTypeConfiguration<ExchangeRateEvidence>
{
    public void Configure(EntityTypeBuilder<ExchangeRateEvidence> builder)
    {
        builder.ToTable("exchange_rate_evidence");
        builder.HasKey(x => x.Id);
        builder.Property(x => x.Id).HasColumnName("id");
        builder.Property(x => x.CompanyId).HasColumnName("company_id");
        builder.Property(x => x.RateSetId).HasColumnName("rate_set_id");
        builder.Property(x => x.Checksum).HasColumnName("checksum").HasMaxLength(64).IsRequired();
        builder.Property(x => x.ProtectedPayload).HasColumnName("protected_payload").HasColumnType("nvarchar(max)").IsRequired();
        builder.Property(x => x.ContentType).HasColumnName("content_type").HasMaxLength(100).IsRequired();
        builder.Property(x => x.RetentionExpiresUtc).HasColumnName("retention_expires_at");
        builder.Property(x => x.CreatedUtc).HasColumnName("created_at");
        builder.HasIndex(x => new { x.CompanyId, x.RateSetId });
        builder.HasIndex(x => new { x.CompanyId, x.Checksum });
        builder.HasIndex(x => x.RetentionExpiresUtc);
        builder.HasOne(x => x.RateSet).WithMany(x => x.Evidence)
            .HasForeignKey(x => new { x.CompanyId, x.RateSetId })
            .HasPrincipalKey(x => new { x.CompanyId, x.Id }).OnDelete(DeleteBehavior.Cascade);
    }
}
