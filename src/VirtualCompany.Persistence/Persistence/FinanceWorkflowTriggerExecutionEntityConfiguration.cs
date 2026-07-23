using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using VirtualCompany.Domain.Entities;

namespace VirtualCompany.Infrastructure.Persistence;

internal sealed class FinanceWorkflowTriggerExecutionEntityConfiguration : IEntityTypeConfiguration<FinanceWorkflowTriggerExecution>
{
    public void Configure(EntityTypeBuilder<FinanceWorkflowTriggerExecution> builder)
    {
        builder.ToTable("finance_workflow_trigger_executions");

        builder.HasKey(x => x.Id);
        builder.HasAlternateKey(x => new { x.CompanyId, x.Id });
        builder.Property(x => x.Id).HasColumnName("id");
        builder.Property(x => x.CompanyId).HasColumnName("company_id").IsRequired();
        builder.Property(x => x.TriggerType).HasColumnName("trigger_type").HasMaxLength(64).IsRequired();
        builder.Property(x => x.SourceEntityType).HasColumnName("source_entity_type").HasMaxLength(128).IsRequired();
        builder.Property(x => x.SourceEntityId).HasColumnName("source_entity_id").HasMaxLength(128).IsRequired();
        builder.Property(x => x.SourceEntityVersion).HasColumnName("source_entity_version").HasMaxLength(256).IsRequired();
        builder.Property(x => x.CorrelationId).HasColumnName("correlation_id").HasMaxLength(128);
        builder.Property(x => x.EventId).HasColumnName("event_id").HasMaxLength(200);
        builder.Property(x => x.CausationId).HasColumnName("causation_id").HasMaxLength(128);
        builder.Property(x => x.TriggerMessageId).HasColumnName("trigger_message_id").HasMaxLength(64);
        builder.Property(x => x.OccurredAtUtc).HasColumnName("occurred_at").IsRequired();
        builder.Property(x => x.StartedAtUtc).HasColumnName("started_at").IsRequired();
        builder.Property(x => x.CompletedAtUtc).HasColumnName("completed_at");
        builder.Property(x => x.ExecutedChecksJson).HasColumnName("executed_checks").HasColumnType("nvarchar(max)").HasDefaultValueSql(CompanyJsonColumnConfiguration.JsonArrayDefault).IsRequired();
        builder.Property(x => x.Outcome).HasColumnName("outcome").HasMaxLength(32).IsRequired();
        builder.Property(x => x.MetadataJson).HasColumnName("metadata_json").HasColumnType("nvarchar(max)").HasDefaultValueSql(CompanyJsonColumnConfiguration.JsonObjectDefault).IsRequired();
        builder.Property(x => x.ErrorDetails).HasColumnName("error_details").HasMaxLength(4000);

        builder.HasIndex(x => x.CompanyId);
        builder.HasIndex(x => new { x.CompanyId, x.CorrelationId });
        builder.HasIndex(x => new { x.CompanyId, x.EventId });
        builder.HasIndex(x => new { x.CompanyId, x.TriggerType, x.OccurredAtUtc });
        builder.HasIndex(x => new { x.CompanyId, x.TriggerType, x.SourceEntityId });
        builder.HasIndex(x => new { x.CompanyId, x.SourceEntityType, x.SourceEntityId });
        builder.HasIndex(x => new { x.CompanyId, x.TriggerType, x.SourceEntityId, x.SourceEntityVersion })
            .HasDatabaseName("IX_finance_workflow_trigger_executions_company_id_trigger_type_source_entity_id_source_entity_version");

        builder.HasOne(x => x.Company)
            .WithMany()
            .HasForeignKey(x => x.CompanyId)
            .OnDelete(DeleteBehavior.Cascade);
    }
}