using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using VirtualCompany.Domain.Entities;

namespace VirtualCompany.Infrastructure.Persistence;

internal sealed class AccountingMigrationRunConfiguration : IEntityTypeConfiguration<AccountingMigrationRun>
{
    public void Configure(EntityTypeBuilder<AccountingMigrationRun> builder)
    {
        builder.ToTable("accounting_migration_runs");
        builder.HasKey(x => x.Id);
        builder.HasAlternateKey(x => new { x.CompanyId, x.Id });
        builder.Property(x => x.Id).HasColumnName("id");
        builder.Property(x => x.CompanyId).HasColumnName("company_id").IsRequired();
        builder.Property(x => x.TargetVersion).HasColumnName("target_version").HasMaxLength(64).IsRequired();
        builder.Property(x => x.IdempotencyKey).HasColumnName("idempotency_key").HasMaxLength(200).IsRequired();
        builder.Property(x => x.Status).HasColumnName("status").HasMaxLength(40).IsRequired();
        builder.Property(x => x.Phase).HasColumnName("phase").HasMaxLength(32).IsRequired();
        builder.Property(x => x.AttemptCount).HasColumnName("attempt_count").IsRequired();
        builder.Property(x => x.ScannedCount).HasColumnName("scanned_count").IsRequired();
        builder.Property(x => x.UpdatedCount).HasColumnName("updated_count").IsRequired();
        builder.Property(x => x.ConflictCount).HasColumnName("conflict_count").IsRequired();
        builder.Property(x => x.ReportCount).HasColumnName("report_count").IsRequired();
        builder.Property(x => x.LeaseOwner).HasColumnName("lease_owner").HasMaxLength(128);
        builder.Property(x => x.LeaseExpiresUtc).HasColumnName("lease_expires_utc");
        builder.Property(x => x.FailureCode).HasColumnName("failure_code").HasMaxLength(100);
        builder.Property(x => x.FailureSummary).HasColumnName("failure_summary").HasMaxLength(1000);
        builder.Property(x => x.RequestedByUserId).HasColumnName("requested_by_user_id").IsRequired();
        builder.Property(x => x.CorrelationId).HasColumnName("correlation_id").HasMaxLength(128);
        builder.Property(x => x.RequestedUtc).HasColumnName("requested_utc").IsRequired();
        builder.Property(x => x.StartedUtc).HasColumnName("started_utc");
        builder.Property(x => x.CompletedUtc).HasColumnName("completed_utc");
        builder.Property(x => x.UpdatedUtc).HasColumnName("updated_utc").IsRequired();
        builder.Property(x => x.Version).HasColumnName("version").IsConcurrencyToken();

        builder.HasIndex(x => new { x.CompanyId, x.IdempotencyKey }).IsUnique();
        builder.HasIndex(x => new { x.CompanyId, x.TargetVersion })
            .IsUnique()
            .HasFilter("status IN ('queued', 'running')");
        builder.HasIndex(x => new { x.CompanyId, x.TargetVersion, x.Status });
        builder.HasIndex(x => new { x.Status, x.LeaseExpiresUtc, x.RequestedUtc });
        builder.HasOne(x => x.Company).WithMany().HasForeignKey(x => x.CompanyId).OnDelete(DeleteBehavior.Cascade);
    }
}
