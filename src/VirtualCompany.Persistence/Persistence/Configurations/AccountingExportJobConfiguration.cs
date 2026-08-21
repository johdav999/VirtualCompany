using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using VirtualCompany.Domain.Entities;

namespace VirtualCompany.Infrastructure.Persistence;

internal sealed class AccountingExportJobConfiguration : IEntityTypeConfiguration<AccountingExportJob>
{
    public void Configure(EntityTypeBuilder<AccountingExportJob> builder)
    {
        builder.ToTable("accounting_export_jobs");
        builder.HasKey(x => x.Id);
        builder.Property(x => x.Id).HasColumnName("id");
        builder.Property(x => x.CompanyId).HasColumnName("company_id").IsRequired();
        builder.Property(x => x.FiscalPeriodId).HasColumnName("fiscal_period_id").IsRequired();
        builder.Property(x => x.RequestedByUserId).HasColumnName("requested_by_user_id").IsRequired();
        builder.Property(x => x.IdempotencyKey).HasColumnName("idempotency_key").HasMaxLength(200).IsRequired();
        builder.Property(x => x.Status).HasColumnName("status").HasMaxLength(24).IsRequired();
        builder.Property(x => x.AttemptCount).HasColumnName("attempt_count").IsRequired();
        builder.Property(x => x.NextAttemptUtc).HasColumnName("next_attempt_at");
        builder.Property(x => x.RequestedUtc).HasColumnName("requested_at").IsRequired();
        builder.Property(x => x.StartedUtc).HasColumnName("started_at");
        builder.Property(x => x.CompletedUtc).HasColumnName("completed_at");
        builder.Property(x => x.ExpiresUtc).HasColumnName("expires_at").IsRequired();
        builder.Property(x => x.Checksum).HasColumnName("checksum").HasMaxLength(64);
        builder.Property(x => x.FileName).HasColumnName("file_name").HasMaxLength(180);
        builder.Property(x => x.MediaType).HasColumnName("media_type").HasMaxLength(100);
        builder.Property(x => x.ContentLength).HasColumnName("content_length");
        builder.Property(x => x.Content).HasColumnName("content").HasColumnType("varbinary(max)");
        builder.Property(x => x.FailureCode).HasColumnName("failure_code").HasMaxLength(100);
        builder.Property(x => x.FailureSummary).HasColumnName("failure_summary").HasMaxLength(1000);
        builder.Property(x => x.UpdatedUtc).HasColumnName("updated_at").IsRequired();
        builder.Property(x => x.RowVersion).HasColumnName("row_version").IsRowVersion();
        builder.HasIndex(x => new { x.CompanyId, x.IdempotencyKey }).IsUnique();
        builder.HasIndex(x => new { x.Status, x.NextAttemptUtc, x.RequestedUtc });
        builder.HasIndex(x => new { x.CompanyId, x.FiscalPeriodId, x.RequestedUtc });
        builder.HasOne(x => x.Company).WithMany().HasForeignKey(x => x.CompanyId).OnDelete(DeleteBehavior.Cascade);
        builder.HasOne(x => x.FiscalPeriod).WithMany().HasForeignKey(x => new { x.CompanyId, x.FiscalPeriodId })
            .HasPrincipalKey(x => new { x.CompanyId, x.Id }).OnDelete(DeleteBehavior.Restrict);
    }
}
