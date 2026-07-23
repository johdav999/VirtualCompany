using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using VirtualCompany.Domain.Entities;
using VirtualCompany.Domain.Enums;

namespace VirtualCompany.Infrastructure.Persistence;

internal sealed class FinanceSeedBackfillRunConfiguration : IEntityTypeConfiguration<FinanceSeedBackfillRun>
{
    public void Configure(EntityTypeBuilder<FinanceSeedBackfillRun> builder)
    {
        builder.ToTable("finance_seed_backfill_runs");

        builder.HasKey(x => x.Id);
        builder.Property(x => x.Id).HasColumnName("id");
        builder.Property(x => x.Status)
            .HasColumnName("status")
            .HasConversion(value => value.ToStorageValue(), value => FinanceSeedBackfillRunStatusValues.Parse(value))
            .HasMaxLength(64)
            .IsRequired();
        builder.Property(x => x.StartedUtc).HasColumnName("started_at").IsRequired();
        builder.Property(x => x.CompletedUtc).HasColumnName("completed_at");
        builder.Property(x => x.ScannedCount).HasColumnName("scanned_count").IsRequired();
        builder.Property(x => x.QueuedCount).HasColumnName("queued_count").IsRequired();
        builder.Property(x => x.SucceededCount).HasColumnName("succeeded_count").IsRequired();
        builder.Property(x => x.SkippedCount).HasColumnName("skipped_count").IsRequired();
        builder.Property(x => x.FailedCount).HasColumnName("failed_count").IsRequired();
        builder.Property(x => x.ConfigurationSnapshotJson).HasColumnName("configuration_snapshot_json").HasColumnType("text").IsRequired();
        builder.Property(x => x.ErrorDetails).HasColumnName("error_details").HasMaxLength(2000);

        builder.HasIndex(x => x.StartedUtc);
    }
}

internal sealed class FinanceSeedBackfillAttemptConfiguration : IEntityTypeConfiguration<FinanceSeedBackfillAttempt>
{
    public void Configure(EntityTypeBuilder<FinanceSeedBackfillAttempt> builder)
    {
        builder.ToTable("finance_seed_backfill_attempts");

        builder.HasKey(x => x.Id);
        builder.Property(x => x.Id).HasColumnName("id");
        builder.Property(x => x.RunId).HasColumnName("run_id").IsRequired();
        builder.Property(x => x.CompanyId).HasColumnName("company_id").IsRequired();
        builder.Property(x => x.BackgroundExecutionId).HasColumnName("background_execution_id");
        builder.Property(x => x.IdempotencyKey).HasColumnName("idempotency_key").HasMaxLength(200);
        builder.Property(x => x.Status)
            .HasColumnName("status")
            .HasConversion(value => value.ToStorageValue(), value => FinanceSeedBackfillAttemptStatusValues.Parse(value))
            .HasMaxLength(64)
            .IsRequired();
        builder.Property(x => x.StartedUtc).HasColumnName("started_at").IsRequired();
        builder.Property(x => x.CompletedUtc).HasColumnName("completed_at");
        builder.Property(x => x.SkipReason).HasColumnName("skip_reason").HasMaxLength(256);
        builder.Property(x => x.ErrorDetails).HasColumnName("error_details").HasMaxLength(2000);
        builder.Property(x => x.SeedStateBefore)
            .HasColumnName("seed_state_before")
            .HasConversion(value => value.ToStorageValue(), value => FinanceSeedingStateValues.Parse(value))
            .HasMaxLength(32)
            .IsRequired();
        builder.Property(x => x.SeedStateAfter)
            .HasColumnName("seed_state_after")
            .HasConversion(
                value => value.HasValue ? value.Value.ToStorageValue() : null,
                value => string.IsNullOrWhiteSpace(value) ? null : FinanceSeedingStateValues.Parse(value))
            .HasMaxLength(32);

        builder.HasIndex(x => x.RunId);
        builder.HasIndex(x => x.CompanyId);
        builder.HasIndex(x => x.BackgroundExecutionId);
        builder.HasIndex(x => new { x.RunId, x.CompanyId }).IsUnique();

        builder.HasOne(x => x.Run)
            .WithMany(x => x.Attempts)
            .HasForeignKey(x => x.RunId)
            .OnDelete(DeleteBehavior.Cascade);

        builder.HasOne(x => x.Company)
            .WithMany()
            .HasForeignKey(x => x.CompanyId)
            .OnDelete(DeleteBehavior.Cascade);
    }
}