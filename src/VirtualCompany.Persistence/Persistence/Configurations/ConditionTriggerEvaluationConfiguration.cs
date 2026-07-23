using System.Text.Json;
using System.Text.Json.Nodes;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using Microsoft.EntityFrameworkCore.ChangeTracking;
using Microsoft.EntityFrameworkCore.Storage.ValueConversion;
using VirtualCompany.Domain.Entities;
using VirtualCompany.Domain.Enums;

namespace VirtualCompany.Infrastructure.Persistence;
internal sealed class ConditionTriggerEvaluationConfiguration : IEntityTypeConfiguration<ConditionTriggerEvaluation>
{
    public void Configure(EntityTypeBuilder<ConditionTriggerEvaluation> builder)
    {
        builder.ToTable("condition_trigger_evaluations");

        builder.HasKey(x => x.Id);
        builder.Property(x => x.Id).HasColumnName("id");
        builder.Property(x => x.CompanyId).HasColumnName("company_id").IsRequired();
        builder.Property(x => x.ConditionDefinitionId).HasColumnName("condition_definition_id").HasMaxLength(200).IsRequired();
        builder.Property(x => x.WorkflowTriggerId).HasColumnName("workflow_trigger_id");
        builder.Property(x => x.EvaluatedUtc).HasColumnName("evaluated_at").IsRequired();
        builder.Property(x => x.SourceType)
            .HasColumnName("source_type")
            .HasConversion(value => value.ToStorageValue(), value => ConditionTriggerStorageValues.ParseSourceType(value))
            .HasMaxLength(32)
            .IsRequired();
        builder.Property(x => x.SourceName).HasColumnName("source_name").HasMaxLength(200);
        builder.Property(x => x.EntityType).HasColumnName("entity_type").HasMaxLength(100);
        builder.Property(x => x.FieldPath).HasColumnName("field_path").HasMaxLength(200);
        builder.Property(x => x.Operator)
            .HasColumnName("operator")
            .HasConversion(value => value.ToStorageValue(), value => ConditionTriggerStorageValues.ParseOperator(value))
            .HasMaxLength(64)
            .IsRequired();
        builder.Property(x => x.ValueType)
            .HasColumnName("value_type")
            .HasConversion(
                value => value.HasValue ? value.Value.ToStorageValue() : null,
                value => string.IsNullOrWhiteSpace(value) ? null : ConditionTriggerStorageValues.ParseValueType(value))
            .HasMaxLength(32);
        builder.Property(x => x.RepeatFiringMode)
            .HasColumnName("repeat_firing_mode")
            .HasConversion(value => value.ToStorageValue(), value => ConditionTriggerStorageValues.ParseRepeatFiringMode(value))
            .HasMaxLength(64)
            .HasDefaultValue(RepeatFiringMode.FalseToTrueTransition)
            .HasSentinel((RepeatFiringMode)0)
            .IsRequired();
        builder.Property(x => x.InputValues)
            .HasColumnName("input_values_json")
            .HasJsonConversion<Dictionary<string, JsonNode?>>()
            .HasDefaultValueSql(CompanyJsonColumnConfiguration.JsonObjectDefault)
            .IsRequired();
        builder.Property(x => x.PreviousOutcome).HasColumnName("previous_outcome");
        builder.Property(x => x.CurrentOutcome).HasColumnName("current_outcome").IsRequired();
        builder.Property(x => x.Fired).HasColumnName("fired").IsRequired();
        builder.Property(x => x.Diagnostic).HasColumnName("diagnostic").HasMaxLength(2000);
        builder.Property(x => x.CreatedUtc).HasColumnName("created_at").IsRequired();

        builder.HasIndex(x => new { x.CompanyId, x.WorkflowTriggerId, x.ConditionDefinitionId, x.EvaluatedUtc });
        builder.HasIndex(x => new { x.CompanyId, x.WorkflowTriggerId, x.EvaluatedUtc });
        builder.HasIndex(x => new { x.CompanyId, x.Fired, x.EvaluatedUtc });

        builder.HasOne(x => x.Company).WithMany().HasForeignKey(x => x.CompanyId).OnDelete(DeleteBehavior.Cascade);
        builder.HasOne(x => x.WorkflowTrigger)
            .WithMany()
            .HasForeignKey(x => x.WorkflowTriggerId)
            .OnDelete(DeleteBehavior.SetNull);
    }
}

