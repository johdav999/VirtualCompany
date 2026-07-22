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
internal sealed class CompanyBriefingContributionConfiguration : IEntityTypeConfiguration<CompanyBriefingContribution>
{
    public void Configure(EntityTypeBuilder<CompanyBriefingContribution> builder)
    {
        builder.ToTable("company_briefing_contributions");

        builder.HasKey(x => x.Id);
        builder.Property(x => x.Id).HasColumnName("id");
        builder.Property(x => x.CompanyId).HasColumnName("company_id").IsRequired();
        builder.Property(x => x.SectionId).HasColumnName("section_id").IsRequired();
        builder.Property(x => x.AgentId).HasColumnName("agent_id").IsRequired();
        builder.Property(x => x.SourceEntityType).HasColumnName("source_entity_type").HasMaxLength(100).IsRequired();
        builder.Property(x => x.SourceEntityId).HasColumnName("source_entity_id").IsRequired();
        builder.Property(x => x.SourceLabel).HasColumnName("source_label").HasMaxLength(300).IsRequired();
        builder.Property(x => x.SourceStatus).HasColumnName("source_status").HasMaxLength(100);
        builder.Property(x => x.SourceRoute).HasColumnName("source_route").HasMaxLength(2048);
        builder.Property(x => x.TimestampUtc).HasColumnName("contributed_at").IsRequired();
        builder.Property(x => x.ConfidenceScore).HasColumnName("confidence_score").HasPrecision(5, 4);
        builder.Property(x => x.ConfidenceMetadata)
            .HasColumnName("confidence_metadata_json")
            .HasJsonConversion<Dictionary<string, JsonNode?>>()
            .HasDefaultValueSql(CompanyJsonColumnConfiguration.JsonObjectDefault)
            .IsRequired();
        builder.Property(x => x.CompanyEntityId).HasColumnName("company_entity_id");
        builder.Property(x => x.WorkflowInstanceId).HasColumnName("workflow_instance_id");
        builder.Property(x => x.TaskId).HasColumnName("task_id");
        builder.Property(x => x.EventCorrelationId).HasColumnName("event_correlation_id").HasMaxLength(128);
        builder.Property(x => x.Topic).HasColumnName("topic").HasMaxLength(300).IsRequired();
        builder.Property(x => x.Narrative).HasColumnName("narrative").IsRequired();
        builder.Property(x => x.Assessment).HasColumnName("assessment").HasMaxLength(200);
        builder.Property(x => x.Metadata)
            .HasColumnName("metadata_json")
            .HasJsonConversion<Dictionary<string, JsonNode?>>()
            .HasDefaultValueSql(CompanyJsonColumnConfiguration.JsonObjectDefault)
            .IsRequired();
        builder.Property(x => x.CreatedUtc).HasColumnName("created_at").IsRequired();

        builder.HasIndex(x => new { x.CompanyId, x.SectionId });
        builder.HasIndex(x => new { x.CompanyId, x.AgentId, x.TimestampUtc });
        builder.HasIndex(x => new { x.CompanyId, x.SourceEntityType, x.SourceEntityId });
        builder.HasIndex(x => new { x.CompanyId, x.CompanyEntityId });
        builder.HasIndex(x => new { x.CompanyId, x.WorkflowInstanceId });
        builder.HasIndex(x => new { x.CompanyId, x.TaskId });
        builder.HasIndex(x => new { x.CompanyId, x.EventCorrelationId });

        builder.HasOne(x => x.Company)
            .WithMany()
            .HasForeignKey(x => x.CompanyId)
            .OnDelete(DeleteBehavior.NoAction);
        builder.HasOne(x => x.Section)
            .WithMany(x => x.Contributions)
            .HasForeignKey(x => x.SectionId)
            .OnDelete(DeleteBehavior.Cascade);
    }
}

