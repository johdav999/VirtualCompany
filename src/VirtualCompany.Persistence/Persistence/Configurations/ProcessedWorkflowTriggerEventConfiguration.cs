using System.Text.Json;
using System.Text.Json.Nodes;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using Microsoft.EntityFrameworkCore.ChangeTracking;
using Microsoft.EntityFrameworkCore.Storage.ValueConversion;
using VirtualCompany.Domain.Entities;
using VirtualCompany.Domain.Enums;

namespace VirtualCompany.Infrastructure.Persistence;
internal sealed class ProcessedWorkflowTriggerEventConfiguration : IEntityTypeConfiguration<ProcessedWorkflowTriggerEvent>
{
    public void Configure(EntityTypeBuilder<ProcessedWorkflowTriggerEvent> builder)
    {
        builder.ToTable("processed_workflow_trigger_events");

        builder.HasKey(x => x.Id);
        builder.Property(x => x.Id).HasColumnName("id");
        builder.Property(x => x.CompanyId).HasColumnName("company_id").IsRequired();
        builder.Property(x => x.WorkflowTriggerId).HasColumnName("workflow_trigger_id").IsRequired();
        builder.Property(x => x.EventId).HasColumnName("event_id").HasMaxLength(200).IsRequired();
        builder.Property(x => x.CreatedWorkflowInstanceId).HasColumnName("created_workflow_instance_id");
        builder.Property(x => x.ProcessedUtc).HasColumnName("processed_at").IsRequired();

        builder.HasIndex(x => new { x.CompanyId, x.WorkflowTriggerId, x.EventId }).IsUnique();
        builder.HasIndex(x => new { x.CompanyId, x.ProcessedUtc });
        builder.HasIndex(x => x.WorkflowTriggerId);
        builder.HasIndex(x => x.CreatedWorkflowInstanceId)
            .HasFilter("created_workflow_instance_id IS NOT NULL");

        builder.HasOne(x => x.Company)
            .WithMany()
            .HasForeignKey(x => x.CompanyId)
            .OnDelete(DeleteBehavior.Cascade);
        builder.HasOne(x => x.WorkflowTrigger)
            .WithMany(x => x.ProcessedEvents)
            .HasForeignKey(x => x.WorkflowTriggerId)
            .OnDelete(DeleteBehavior.NoAction);
        builder.HasOne(x => x.CreatedWorkflowInstance)
            .WithMany()
            .HasForeignKey(x => x.CreatedWorkflowInstanceId)
            .OnDelete(DeleteBehavior.SetNull);
    }
}

