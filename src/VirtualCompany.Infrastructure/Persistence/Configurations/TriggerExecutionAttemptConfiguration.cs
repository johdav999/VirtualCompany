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
internal sealed class TriggerExecutionAttemptConfiguration : IEntityTypeConfiguration<TriggerExecutionAttempt>
{
    public void Configure(EntityTypeBuilder<TriggerExecutionAttempt> builder)
    {
        builder.ToTable("trigger_execution_attempts");

        builder.HasKey(x => x.Id);
        builder.Property(x => x.Id).HasColumnName("id");
        builder.Property(x => x.CompanyId).HasColumnName("company_id").IsRequired();
        builder.Property(x => x.TriggerId).HasColumnName("trigger_id").IsRequired();
        builder.Property(x => x.TriggerType).HasColumnName("trigger_type").HasMaxLength(64).IsRequired();
        builder.Property(x => x.AgentId).HasColumnName("agent_id");
        builder.Property(x => x.OccurrenceUtc).HasColumnName("occurrence_at").IsRequired();
        builder.Property(x => x.CorrelationId).HasColumnName("correlation_id").HasMaxLength(128).IsRequired();
        builder.Property(x => x.IdempotencyKey).HasColumnName("idempotency_key").HasMaxLength(200).IsRequired();
        builder.Property(x => x.Status)
            .HasColumnName("status")
            .HasConversion(value => value.ToStorageValue(), value => TriggerExecutionAttemptStatusValues.Parse(value))
            .HasMaxLength(32)
            .HasDefaultValue(TriggerExecutionAttemptStatusValues.DefaultStatus)
            .HasSentinel((TriggerExecutionAttemptStatus)0)
            .IsRequired();
        builder.Property(x => x.DenialReason).HasColumnName("denial_reason").HasMaxLength(2000);
        builder.Property(x => x.RetryAttemptCount).HasColumnName("retry_attempt_count").HasDefaultValue(1).IsRequired();
        builder.Property(x => x.FailureDetails).HasColumnName("failure_details").HasMaxLength(4000);
        builder.Property(x => x.DispatchReferenceType).HasColumnName("dispatch_reference_type").HasMaxLength(100);
        builder.Property(x => x.DispatchReferenceId).HasColumnName("dispatch_reference_id").HasMaxLength(128);
        builder.Property(x => x.CreatedUtc).HasColumnName("created_at").IsRequired();
        builder.Property(x => x.UpdatedUtc).HasColumnName("updated_at").IsRequired();
        builder.Property(x => x.NextRetryUtc).HasColumnName("next_retry_at");
        builder.Property(x => x.CompletedUtc).HasColumnName("completed_at");

        builder.HasIndex(x => x.IdempotencyKey).IsUnique();
        builder.HasIndex(x => new { x.CompanyId, x.TriggerType, x.Status, x.OccurrenceUtc });
        builder.HasIndex(x => new { x.CompanyId, x.AgentId, x.OccurrenceUtc });
        builder.HasIndex(x => new { x.CompanyId, x.CorrelationId });

        builder.HasOne(x => x.Company)
            .WithMany()
            .HasForeignKey(x => x.CompanyId)
            .OnDelete(DeleteBehavior.Cascade);
        builder.HasOne(x => x.Agent)
            .WithMany()
            .HasForeignKey(x => x.AgentId)
            .OnDelete(DeleteBehavior.SetNull);
    }
}

