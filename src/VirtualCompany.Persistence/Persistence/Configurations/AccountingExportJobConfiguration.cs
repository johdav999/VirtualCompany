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
        builder.Property(x => x.ExportType).HasColumnName("export_type").HasMaxLength(48).IsRequired();
        builder.Property(x => x.SpecificationVersion).HasColumnName("specification_version").HasMaxLength(64);
        builder.Property(x => x.InputChecksum).HasColumnName("input_checksum").HasMaxLength(64);
        builder.Property(x => x.EncodingName).HasColumnName("encoding_name").HasMaxLength(64);
        builder.Property(x => x.StorageKey).HasColumnName("storage_key").HasMaxLength(500);
        builder.Property(x => x.ManifestJson).HasColumnName("manifest_json").HasColumnType("nvarchar(max)");
        builder.Property(x => x.CorrelationId).HasColumnName("correlation_id").HasMaxLength(128);
        builder.Property(x => x.SourceAccountCount).HasColumnName("source_account_count");
        builder.Property(x => x.SourceJournalCount).HasColumnName("source_journal_count");
        builder.Property(x => x.SourceLineCount).HasColumnName("source_line_count");
        builder.Property(x => x.SourceDebitTotal).HasColumnName("source_debit_total").HasPrecision(19, 6);
        builder.Property(x => x.SourceCreditTotal).HasColumnName("source_credit_total").HasPrecision(19, 6);
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
        builder.Property(x => x.LeaseOwner).HasColumnName("lease_owner").HasMaxLength(128);
        builder.Property(x => x.LeaseExpiresUtc).HasColumnName("lease_expires_at");
        builder.Property(x => x.UpdatedUtc).HasColumnName("updated_at").IsRequired();
        builder.Property(x => x.RowVersion).HasColumnName("row_version").IsRowVersion();
        builder.HasIndex(x => new { x.CompanyId, x.IdempotencyKey }).IsUnique();
        builder.HasIndex(x => new { x.Status, x.NextAttemptUtc, x.RequestedUtc });
        builder.HasIndex(x => new { x.CompanyId, x.FiscalPeriodId, x.RequestedUtc });
        builder.HasIndex(x => new { x.CompanyId, x.FiscalPeriodId, x.ExportType, x.InputChecksum });
        builder.HasIndex(x => new { x.CompanyId, x.Status, x.ExpiresUtc });
        builder.HasOne(x => x.Company).WithMany().HasForeignKey(x => x.CompanyId).OnDelete(DeleteBehavior.Cascade);
        builder.HasOne(x => x.FiscalPeriod).WithMany().HasForeignKey(x => new { x.CompanyId, x.FiscalPeriodId })
            .HasPrincipalKey(x => new { x.CompanyId, x.Id }).OnDelete(DeleteBehavior.Restrict);
    }
}
