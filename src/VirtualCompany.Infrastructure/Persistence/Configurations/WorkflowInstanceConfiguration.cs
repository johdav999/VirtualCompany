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
internal sealed class WorkflowInstanceConfiguration : IEntityTypeConfiguration<WorkflowInstance>
{
    public void Configure(EntityTypeBuilder<WorkflowInstance> builder)
    {
        builder.ToTable("workflow_instances");

        builder.HasKey(x => x.Id);
        builder.Property(x => x.Id).HasColumnName("id");
        builder.Property(x => x.CompanyId).HasColumnName("company_id").IsRequired();
        builder.Property(x => x.DefinitionId).HasColumnName("definition_id").IsRequired();
        builder.Property(x => x.TriggerId).HasColumnName("trigger_id");
        builder.Property(x => x.TriggerSource)
            .HasColumnName("trigger_source")
            .HasConversion(value => value.ToStorageValue(), value => WorkflowTriggerTypeValues.Parse(value))
            .HasMaxLength(32)
            .HasDefaultValue(WorkflowTriggerType.Manual)
            .HasSentinel((WorkflowTriggerType)0)
            .IsRequired();
        builder.Property(x => x.TriggerRef).HasColumnName("trigger_ref").HasMaxLength(200);
        builder.Property(x => x.State)
            .HasColumnName("state")
            .HasConversion(value => value.ToStorageValue(), value => WorkflowInstanceStatusValues.Parse(value))
            .HasMaxLength(32)
            .HasDefaultValue(WorkflowInstanceStatusValues.DefaultStatus)
            .HasSentinel((WorkflowInstanceStatus)0)
            .IsRequired();
        builder.Property(x => x.CurrentStep).HasColumnName("current_step").HasMaxLength(200);
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
        builder.Property(x => x.ContextJson)
            .HasColumnName("context_json")
            .HasJsonConversion<Dictionary<string, JsonNode?>>()
            .HasDefaultValueSql(CompanyJsonColumnConfiguration.JsonObjectDefault)
            .IsRequired();
        builder.Property(x => x.StartedUtc).HasColumnName("started_at").IsRequired();
        builder.Property(x => x.UpdatedUtc).HasColumnName("updated_at").IsRequired();
        builder.Property(x => x.CompletedUtc).HasColumnName("completed_at");

        builder.HasIndex(x => new { x.CompanyId, x.DefinitionId, x.StartedUtc });
        builder.HasIndex(x => new { x.CompanyId, x.State });
        builder.HasIndex(x => new { x.CompanyId, x.State, x.UpdatedUtc });
        builder.HasIndex(x => new { x.State, x.UpdatedUtc });
        builder.HasIndex(x => new { x.CompanyId, x.DefinitionId, x.TriggerSource, x.TriggerRef })
            .HasFilter("trigger_ref IS NOT NULL")
            .IsUnique();
        builder.HasIndex(x => new { x.CompanyId, x.UpdatedUtc });

        builder.HasOne(x => x.Company).WithMany().HasForeignKey(x => x.CompanyId).OnDelete(DeleteBehavior.Cascade);
        builder.HasOne(x => x.Definition).WithMany(x => x.Instances).HasForeignKey(x => x.DefinitionId).OnDelete(DeleteBehavior.Restrict);
        builder.HasOne(x => x.Trigger).WithMany().HasForeignKey(x => x.TriggerId).OnDelete(DeleteBehavior.Restrict);
    }
}
