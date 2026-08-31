using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using VirtualCompany.Domain.Entities;

namespace VirtualCompany.Infrastructure.Persistence.Configurations;

public sealed class ExchangeRateConversionConfiguration : IEntityTypeConfiguration<ExchangeRateConversion>
{
    public void Configure(EntityTypeBuilder<ExchangeRateConversion> builder)
    {
        builder.ToTable("exchange_rate_conversions");
        builder.HasKey(x => x.Id);
        builder.HasAlternateKey(x => new { x.CompanyId, x.Id });
        builder.Property(x => x.Id).HasColumnName("id");
        builder.Property(x => x.CompanyId).HasColumnName("company_id");
        builder.Property(x => x.IdempotencyKey).HasColumnName("idempotency_key").HasMaxLength(200).IsRequired();
        builder.Property(x => x.RequestHash).HasColumnName("request_hash").HasMaxLength(64).IsRequired();
        builder.Property(x => x.Purpose).HasColumnName("purpose").HasMaxLength(32).IsRequired();
        builder.Property(x => x.RequestedDate).HasColumnName("requested_date");
        builder.Property(x => x.InputAmount).HasColumnName("input_amount").HasPrecision(38, 18);
        builder.Property(x => x.InputCurrency).HasColumnName("input_currency").HasMaxLength(3).IsRequired();
        builder.Property(x => x.OutputCurrency).HasColumnName("output_currency").HasMaxLength(3).IsRequired();
        builder.Property(x => x.EffectiveRate).HasColumnName("effective_rate").HasPrecision(38, 18);
        builder.Property(x => x.UnroundedAmount).HasColumnName("unrounded_amount").HasPrecision(38, 18);
        builder.Property(x => x.RoundedAmount).HasColumnName("rounded_amount").HasPrecision(38, 18);
        builder.Property(x => x.RoundingResidual).HasColumnName("rounding_residual").HasPrecision(38, 18);
        builder.Property(x => x.OutputPrecision).HasColumnName("output_precision");
        builder.Property(x => x.RoundingMode).HasColumnName("rounding_mode").HasMaxLength(32).IsRequired();
        builder.Property(x => x.CreatedUtc).HasColumnName("created_at");
        builder.HasIndex(x => new { x.CompanyId, x.IdempotencyKey }).IsUnique();
        builder.HasIndex(x => new { x.CompanyId, x.RequestedDate, x.InputCurrency, x.OutputCurrency });
    }
}

public sealed class ExchangeRateConversionLegConfiguration : IEntityTypeConfiguration<ExchangeRateConversionLeg>
{
    public void Configure(EntityTypeBuilder<ExchangeRateConversionLeg> builder)
    {
        builder.ToTable("exchange_rate_conversion_legs");
        builder.HasKey(x => x.Id);
        builder.Property(x => x.Id).HasColumnName("id");
        builder.Property(x => x.CompanyId).HasColumnName("company_id");
        builder.Property(x => x.ConversionId).HasColumnName("conversion_id");
        builder.Property(x => x.Sequence).HasColumnName("sequence");
        builder.Property(x => x.ObservationId).HasColumnName("observation_id");
        builder.Property(x => x.FromCurrency).HasColumnName("from_currency").HasMaxLength(3).IsRequired();
        builder.Property(x => x.ToCurrency).HasColumnName("to_currency").HasMaxLength(3).IsRequired();
        builder.Property(x => x.Factor).HasColumnName("factor").HasPrecision(38, 18);
        builder.HasIndex(x => new { x.CompanyId, x.ConversionId, x.Sequence }).IsUnique();
        builder.HasIndex(x => new { x.CompanyId, x.ObservationId });
        builder.HasOne(x => x.Conversion).WithMany(x => x.Legs)
            .HasForeignKey(x => new { x.CompanyId, x.ConversionId })
            .HasPrincipalKey(x => new { x.CompanyId, x.Id }).OnDelete(DeleteBehavior.Cascade);
        builder.HasOne(x => x.Observation).WithMany()
            .HasForeignKey(x => new { x.CompanyId, x.ObservationId })
            .HasPrincipalKey(x => new { x.CompanyId, x.Id }).OnDelete(DeleteBehavior.Restrict);
    }
}
