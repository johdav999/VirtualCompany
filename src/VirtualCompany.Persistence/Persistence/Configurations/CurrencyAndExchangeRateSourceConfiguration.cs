using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using VirtualCompany.Domain.Entities;

namespace VirtualCompany.Infrastructure.Persistence.Configurations;

public sealed class CompanyCurrencyDefinitionConfiguration : IEntityTypeConfiguration<CompanyCurrencyDefinition>
{
    public void Configure(EntityTypeBuilder<CompanyCurrencyDefinition> builder)
    {
        builder.ToTable("company_currency_definitions");
        builder.HasKey(x => x.Id);
        builder.HasAlternateKey(x => new { x.CompanyId, x.Id });
        builder.Property(x => x.Id).HasColumnName("id");
        builder.Property(x => x.CompanyId).HasColumnName("company_id");
        builder.Property(x => x.Code).HasColumnName("code").HasMaxLength(3).IsRequired();
        builder.Property(x => x.Name).HasColumnName("name").HasMaxLength(100).IsRequired();
        builder.Property(x => x.MinorUnitPrecision).HasColumnName("minor_unit_precision");
        builder.Property(x => x.IsEnabled).HasColumnName("is_enabled");
        builder.Property(x => x.Version).HasColumnName("version").IsConcurrencyToken();
        builder.Property(x => x.CreatedUtc).HasColumnName("created_at");
        builder.Property(x => x.UpdatedUtc).HasColumnName("updated_at");
        builder.HasIndex(x => new { x.CompanyId, x.Code }).IsUnique();
        builder.HasIndex(x => new { x.CompanyId, x.IsEnabled, x.Code });
    }
}

public sealed class ExchangeRateSourceConfiguration : IEntityTypeConfiguration<ExchangeRateSource>
{
    public void Configure(EntityTypeBuilder<ExchangeRateSource> builder)
    {
        builder.ToTable("exchange_rate_sources");
        builder.HasKey(x => x.Id);
        builder.HasAlternateKey(x => new { x.CompanyId, x.Id });
        builder.Property(x => x.Id).HasColumnName("id");
        builder.Property(x => x.CompanyId).HasColumnName("company_id");
        builder.Property(x => x.SourceKey).HasColumnName("source_key").HasMaxLength(64).IsRequired();
        builder.Property(x => x.DisplayName).HasColumnName("display_name").HasMaxLength(160).IsRequired();
        builder.Property(x => x.SourceKind).HasColumnName("source_kind").HasMaxLength(32).IsRequired();
        builder.Property(x => x.SourceVersion).HasColumnName("source_version").HasMaxLength(64).IsRequired();
        builder.Property(x => x.Priority).HasColumnName("priority");
        builder.Property(x => x.RequiresApproval).HasColumnName("requires_approval");
        builder.Property(x => x.MaxStalenessDays).HasColumnName("max_staleness_days");
        builder.Property(x => x.RefreshIntervalHours).HasColumnName("refresh_interval_hours");
        builder.Property(x => x.LicenseSummary).HasColumnName("license_summary").HasMaxLength(1000).IsRequired();
        builder.Property(x => x.IsEnabled).HasColumnName("is_enabled");
        builder.Property(x => x.LastAttemptUtc).HasColumnName("last_attempt_at");
        builder.Property(x => x.LastSuccessfulRefreshUtc).HasColumnName("last_successful_refresh_at");
        builder.Property(x => x.NextRefreshUtc).HasColumnName("next_refresh_at");
        builder.Property(x => x.LastFailureReasonCode).HasColumnName("last_failure_reason_code").HasMaxLength(96);
        builder.Property(x => x.LastFailureSummary).HasColumnName("last_failure_summary").HasMaxLength(1000);
        builder.Property(x => x.Version).HasColumnName("version").IsConcurrencyToken();
        builder.Property(x => x.CreatedUtc).HasColumnName("created_at");
        builder.Property(x => x.UpdatedUtc).HasColumnName("updated_at");
        builder.HasIndex(x => new { x.CompanyId, x.SourceKey }).IsUnique();
        builder.HasIndex(x => new { x.IsEnabled, x.NextRefreshUtc, x.SourceKind });
        builder.HasIndex(x => new { x.CompanyId, x.Priority, x.IsEnabled });
    }
}
