using System.Text.Json;
using System.Text.Json.Nodes;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using Microsoft.EntityFrameworkCore.ChangeTracking;
using Microsoft.EntityFrameworkCore.Storage.ValueConversion;
using VirtualCompany.Domain.Entities;
using VirtualCompany.Infrastructure.Companies;
using VirtualCompany.Domain.Enums;

namespace VirtualCompany.Infrastructure.Persistence;
internal sealed class CompanyBriefingUpdateJobConfiguration : IEntityTypeConfiguration<CompanyBriefingUpdateJob>
{
    public void Configure(EntityTypeBuilder<CompanyBriefingUpdateJob> builder)
    {
        builder.ToTable("company_briefing_update_jobs");

        builder.HasKey(x => x.Id);
        builder.Property(x => x.Id).HasColumnName("id");
        builder.Property(x => x.CompanyId).HasColumnName("company_id").IsRequired();
        builder.Property(x => x.TriggerType)
            .HasColumnName("trigger_type")
            .HasConversion(value => value.ToStorageValue(), value => CompanyBriefingUpdateJobTriggerTypeValues.Parse(value))
            .HasMaxLength(32)
            .IsRequired();
        builder.Property(x => x.BriefingType)
            .HasColumnName("briefing_type")
            .HasConversion(
                value => value.HasValue ? value.Value.ToStorageValue() : null,
                value => string.IsNullOrWhiteSpace(value) ? null : CompanyBriefingTypeValues.Parse(value))
            .HasMaxLength(32);
        builder.Property(x => x.EventType).HasColumnName("event_type").HasMaxLength(100);
        builder.Property(x => x.CorrelationId).HasColumnName("correlation_id").HasMaxLength(128).IsRequired();
        builder.Property(x => x.IdempotencyKey).HasColumnName("idempotency_key").HasMaxLength(300).IsRequired();
        builder.Property(x => x.Status)
            .HasColumnName("status")
            .HasConversion(value => value.ToStorageValue(), value => CompanyBriefingUpdateJobStatusValues.Parse(value))
            .HasMaxLength(32)
            .IsRequired();
        builder.Property(x => x.AttemptCount).HasColumnName("attempt_count").HasDefaultValue(0).IsRequired();
        builder.Property(x => x.MaxAttempts).HasColumnName("max_attempts").HasDefaultValue(5).IsRequired();
        builder.Property(x => x.NextAttemptAt).HasColumnName("next_attempt_at");
        builder.Property(x => x.LastErrorCode).HasColumnName("last_error_code").HasMaxLength(256);
        builder.Property(x => x.LastError).HasColumnName("last_error").HasMaxLength(4000);
        builder.Property(x => x.LastErrorDetails).HasColumnName("last_error_details").HasMaxLength(12000);
        builder.Property(x => x.LastFailureAt).HasColumnName("last_failure_at");
        builder.Property(x => x.StartedAt).HasColumnName("started_at");
        builder.Property(x => x.CompletedAt).HasColumnName("completed_at");
        builder.Property(x => x.FinalFailedAt).HasColumnName("final_failed_at");
        builder.Property(x => x.CreatedAt).HasColumnName("created_at").IsRequired();
        builder.Property(x => x.UpdatedAt).HasColumnName("updated_at").IsRequired();
        builder.Property(x => x.SourceMetadata)
            .HasColumnName("source_metadata_json")
            .HasJsonConversion<Dictionary<string, JsonNode?>>()
            .HasDefaultValueSql(CompanyJsonColumnConfiguration.JsonObjectDefault)
            .IsRequired();

        builder.HasIndex(x => new { x.CompanyId, x.IdempotencyKey }).IsUnique();
        builder.HasIndex(x => new { x.Status, x.NextAttemptAt, x.StartedAt, x.CreatedAt });
        builder.HasIndex(x => new { x.CompanyId, x.Status, x.CreatedAt });
        builder.HasIndex(x => new { x.CompanyId, x.EventType, x.CreatedAt });

        builder.HasOne(x => x.Company)
            .WithMany()
            .HasForeignKey(x => x.CompanyId)
            .OnDelete(DeleteBehavior.Cascade);
    }
}

