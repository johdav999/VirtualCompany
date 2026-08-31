using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using VirtualCompany.Domain.Entities;

namespace VirtualCompany.Infrastructure.Persistence.Configurations;

public sealed class ExchangeRateRefreshJobConfiguration : IEntityTypeConfiguration<ExchangeRateRefreshJob>
{
    public void Configure(EntityTypeBuilder<ExchangeRateRefreshJob> builder)
    {
        builder.ToTable("exchange_rate_refresh_jobs");
        builder.HasKey(x => x.Id);
        builder.Property(x => x.Id).HasColumnName("id");
        builder.Property(x => x.CompanyId).HasColumnName("company_id");
        builder.Property(x => x.SourceId).HasColumnName("source_id");
        builder.Property(x => x.IdempotencyKey).HasColumnName("idempotency_key").HasMaxLength(200).IsRequired();
        builder.Property(x => x.RequestedDate).HasColumnName("requested_date");
        builder.Property(x => x.RequestedCurrencies).HasColumnName("requested_currencies").HasMaxLength(1000).IsRequired();
        builder.Property(x => x.RequestedByUserId).HasColumnName("requested_by_user_id");
        builder.Property(x => x.CorrelationId).HasColumnName("correlation_id").HasMaxLength(128);
        builder.Property(x => x.Status).HasColumnName("status").HasMaxLength(32).IsRequired();
        builder.Property(x => x.AttemptCount).HasColumnName("attempt_count");
        builder.Property(x => x.NextAttemptUtc).HasColumnName("next_attempt_at");
        builder.Property(x => x.LeaseOwner).HasColumnName("lease_owner").HasMaxLength(128);
        builder.Property(x => x.LeaseExpiresUtc).HasColumnName("lease_expires_at");
        builder.Property(x => x.FailureReasonCode).HasColumnName("failure_reason_code").HasMaxLength(96);
        builder.Property(x => x.FailureSummary).HasColumnName("failure_summary").HasMaxLength(1000);
        builder.Property(x => x.RateSetId).HasColumnName("rate_set_id");
        builder.Property(x => x.Version).HasColumnName("version").IsConcurrencyToken();
        builder.Property(x => x.CreatedUtc).HasColumnName("created_at");
        builder.Property(x => x.UpdatedUtc).HasColumnName("updated_at");
        builder.HasIndex(x => new { x.CompanyId, x.IdempotencyKey }).IsUnique();
        builder.HasIndex(x => new { x.Status, x.NextAttemptUtc, x.LeaseExpiresUtc });
        builder.HasIndex(x => new { x.CompanyId, x.SourceId, x.CreatedUtc });
        builder.HasOne(x => x.Source).WithMany()
            .HasForeignKey(x => new { x.CompanyId, x.SourceId })
            .HasPrincipalKey(x => new { x.CompanyId, x.Id }).OnDelete(DeleteBehavior.Restrict);
        builder.HasOne<ExchangeRateSet>().WithMany()
            .HasForeignKey(x => new { x.CompanyId, x.RateSetId })
            .HasPrincipalKey(x => new { x.CompanyId, x.Id }).OnDelete(DeleteBehavior.NoAction);
    }
}
