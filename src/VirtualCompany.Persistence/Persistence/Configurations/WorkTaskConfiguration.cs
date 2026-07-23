using System.Text.Json;
using System.Text.Json.Nodes;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using Microsoft.EntityFrameworkCore.ChangeTracking;
using Microsoft.EntityFrameworkCore.Storage.ValueConversion;
using VirtualCompany.Domain.Entities;
using VirtualCompany.Domain.Enums;

namespace VirtualCompany.Infrastructure.Persistence;
internal sealed class WorkTaskConfiguration : IEntityTypeConfiguration<WorkTask>
{
    public void Configure(EntityTypeBuilder<WorkTask> builder)
    {
        builder.ToTable("tasks");

        builder.HasKey(x => x.Id);
        builder.Property(x => x.Id).HasColumnName("id");
        builder.Property(x => x.CompanyId).HasColumnName("company_id").IsRequired();
        builder.Property(x => x.AssignedAgentId).HasColumnName("assigned_agent_id");
        builder.Property(x => x.ParentTaskId).HasColumnName("parent_task_id");
        builder.Property(x => x.WorkflowInstanceId).HasColumnName("workflow_instance_id");
        builder.Property(x => x.CreatedByActorType).HasColumnName("created_by_actor_type").HasMaxLength(64).IsRequired();
        builder.Property(x => x.CreatedByActorId).HasColumnName("created_by_actor_id");
        builder.Property(x => x.Type).HasColumnName("type").HasMaxLength(100).IsRequired();
        builder.Property(x => x.Title).HasColumnName("title").HasMaxLength(200).IsRequired();
        builder.Property(x => x.Description).HasColumnName("description").HasMaxLength(4000);
        builder.Property(x => x.Priority)
            .HasColumnName("priority")
            .HasConversion(value => value.ToStorageValue(), value => WorkTaskPriorityValues.Parse(value))
            .HasMaxLength(32)
            .HasDefaultValue(WorkTaskPriorityValues.DefaultPriority)
            .HasSentinel((WorkTaskPriority)0)
            .IsRequired();
        builder.Property(x => x.Status)
            .HasColumnName("status")
            .HasConversion(value => value.ToStorageValue(), value => WorkTaskStatusValues.Parse(value))
            .HasMaxLength(32)
            .HasDefaultValue(WorkTaskStatusValues.DefaultStatus)
            .HasSentinel((WorkTaskStatus)0)
            .IsRequired();
        builder.Property(x => x.DueUtc).HasColumnName("due_at");
        builder.Property(x => x.InputPayload)
            .HasColumnName("input_payload")
            .HasJsonConversion<Dictionary<string, JsonNode?>>()
            .HasDefaultValueSql(CompanyJsonColumnConfiguration.JsonObjectDefault)
            .IsRequired();
        builder.Property(x => x.OutputPayload)
            .HasColumnName("output_payload")
            .HasJsonConversion<Dictionary<string, JsonNode?>>()
            .HasDefaultValueSql(CompanyJsonColumnConfiguration.JsonObjectDefault)
            .IsRequired();
        builder.Property(x => x.RationaleSummary).HasColumnName("rationale_summary").HasMaxLength(2000);
        builder.Property(x => x.ConfidenceScore).HasColumnName("confidence_score").HasColumnType("numeric(5,4)");
        builder.Property(x => x.CorrelationId).HasColumnName("correlation_id").HasMaxLength(128);
        builder.Property(x => x.SourceType).HasColumnName("source_type").HasMaxLength(64).HasDefaultValue(WorkTaskSourceTypes.User).IsRequired();
        builder.Property(x => x.OriginatingAgentId).HasColumnName("originating_agent_id");
        builder.Property(x => x.TriggerSource).HasColumnName("trigger_source").HasMaxLength(128);
        builder.Property(x => x.CreationReason).HasColumnName("creation_reason").HasMaxLength(2000);
        builder.Property(x => x.TriggerEventId).HasColumnName("trigger_event_id").HasMaxLength(200);
        builder.Property(x => x.CreatedUtc).HasColumnName("created_at").IsRequired();
        builder.Property(x => x.UpdatedUtc).HasColumnName("updated_at").IsRequired();
        builder.Property(x => x.CompletedUtc).HasColumnName("completed_at");
        builder.Property(x => x.SourceLifecycleVersion).HasColumnName("source_lifecycle_version").HasDefaultValue(0).IsRequired();

        builder.HasIndex(x => new { x.CompanyId, x.Status });
        builder.HasIndex(x => new { x.CompanyId, x.AssignedAgentId });
        builder.HasIndex(x => new { x.CompanyId, x.DueUtc });
        builder.HasIndex(x => new { x.CompanyId, x.ParentTaskId });
        builder.HasIndex(x => new { x.CompanyId, x.Status, x.UpdatedUtc });
        builder.HasIndex(x => new { x.CompanyId, x.AssignedAgentId, x.Status, x.CompletedUtc });
        builder.HasIndex(x => new { x.CompanyId, x.WorkflowInstanceId });
        builder.HasIndex(x => new { x.CompanyId, x.CorrelationId });
        builder.HasIndex(x => new { x.CompanyId, x.UpdatedUtc });
        builder.HasIndex(x => new { x.CompanyId, x.TriggerSource, x.TriggerEventId, x.CorrelationId, x.CreatedUtc });
        builder.HasIndex(x => new { x.CompanyId, x.OriginatingAgentId, x.CreatedUtc });

        builder.HasOne(x => x.Company)
            .WithMany()
            .HasForeignKey(x => x.CompanyId)
            .OnDelete(DeleteBehavior.Cascade);
        builder.HasOne(x => x.AssignedAgent)
            .WithMany()
            .HasForeignKey(x => x.AssignedAgentId)
            .OnDelete(DeleteBehavior.Restrict);
        builder.HasOne(x => x.ParentTask)
            .WithMany(x => x.Subtasks)
            .HasForeignKey(x => x.ParentTaskId)
            .OnDelete(DeleteBehavior.Restrict);
        builder.HasOne(x => x.WorkflowInstance)
            .WithMany(x => x.Tasks)
            .HasForeignKey(x => x.WorkflowInstanceId)
            .OnDelete(DeleteBehavior.Restrict);
        builder.HasMany(x => x.ConversationLinks)
            .WithOne(x => x.Task)
            .HasForeignKey(x => x.TaskId)
            .OnDelete(DeleteBehavior.Restrict);
    }
}

