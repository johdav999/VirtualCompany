using System.Text.Json;
using System.Text.Json.Nodes;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using Microsoft.EntityFrameworkCore.ChangeTracking;
using Microsoft.EntityFrameworkCore.Storage.ValueConversion;
using VirtualCompany.Domain.Entities;
using VirtualCompany.Domain.Enums;

namespace VirtualCompany.Infrastructure.Persistence;
internal sealed class BackgroundExecutionConfiguration : IEntityTypeConfiguration<BackgroundExecution>
{
    public void Configure(EntityTypeBuilder<BackgroundExecution> builder)
    {
        builder.ToTable("background_executions");

        builder.HasKey(x => x.Id);
        builder.Property(x => x.Id).HasColumnName("id");
        builder.Property(x => x.CompanyId).HasColumnName("company_id").IsRequired();
        builder.Property(x => x.ExecutionType)
            .HasColumnName("execution_type")
            .HasConversion(value => value.ToStorageValue(), value => BackgroundExecutionTypeValues.Parse(value))
            .HasMaxLength(64)
            .IsRequired();
        builder.Property(x => x.RelatedEntityType).HasColumnName("related_entity_type").HasMaxLength(100).IsRequired();
        builder.Property(x => x.RelatedEntityId).HasColumnName("related_entity_id").HasMaxLength(128).IsRequired();
        builder.Property(x => x.CorrelationId).HasColumnName("correlation_id").HasMaxLength(128).IsRequired();
        builder.Property(x => x.IdempotencyKey).HasColumnName("idempotency_key").HasMaxLength(200).IsRequired();
        builder.Property(x => x.Status)
            .HasColumnName("status")
            .HasConversion(value => value.ToStorageValue(), value => BackgroundExecutionStatusValues.Parse(value))
            .HasMaxLength(32)
            .HasDefaultValue(BackgroundExecutionStatusValues.DefaultStatus)
            .HasSentinel((BackgroundExecutionStatus)0)
            .IsRequired();
        builder.Property(x => x.AttemptCount).HasColumnName("attempt_count").HasDefaultValue(0).IsRequired();
        builder.Property(x => x.MaxAttempts).HasColumnName("max_attempts").IsRequired();
        builder.Property(x => x.NextRetryUtc).HasColumnName("next_retry_at");
        builder.Property(x => x.StartedUtc).HasColumnName("started_at");
        builder.Property(x => x.HeartbeatUtc).HasColumnName("heartbeat_at");
        builder.Property(x => x.CompletedUtc).HasColumnName("completed_at");
        builder.Property(x => x.FailureCategory)
            .HasColumnName("failure_category")
            .HasConversion(
                value => value.HasValue ? value.Value.ToStorageValue() : null,
                value => string.IsNullOrWhiteSpace(value) ? null : BackgroundExecutionFailureCategoryValues.Parse(value));
        builder.Property(x => x.FailureCode).HasColumnName("failure_code").HasMaxLength(100);
        builder.Property(x => x.FailureMessage).HasColumnName("failure_message").HasMaxLength(4000);
        builder.Property(x => x.EscalationId).HasColumnName("escalation_id");
        builder.Property(x => x.CreatedUtc).HasColumnName("created_at").IsRequired();
        builder.Property(x => x.UpdatedUtc).HasColumnName("updated_at").IsRequired();

        builder.HasIndex(x => new { x.CompanyId, x.Status, x.NextRetryUtc });
        builder.HasIndex(x => new { x.CompanyId, x.RelatedEntityType, x.RelatedEntityId });
        builder.HasIndex(x => new { x.CompanyId, x.ExecutionType, x.IdempotencyKey }).IsUnique();
        builder.HasIndex(x => new { x.Status, x.HeartbeatUtc });
        builder.HasIndex(x => x.CorrelationId);

        builder.HasOne(x => x.Company)
            .WithMany()
            .HasForeignKey(x => x.CompanyId)
            .OnDelete(DeleteBehavior.Cascade);
    }
}

